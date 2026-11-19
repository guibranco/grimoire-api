using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Backups;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public sealed class BackupsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>,
        IDisposable
{
    private const string BackupKey = "backup-key-for-integration-tests-32!";
    private readonly string _backupDir = Path.Combine(
        Path.GetTempPath(),
        $"grimoire-backups-{Guid.NewGuid():N}"
    );

    private WebApplicationFactory<Program> WithBackups(int retention = 5) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Backup:Enabled"] = "true",
                            ["Backup:Key"] = BackupKey,
                            ["Backup:Schedule"] = "", // no background runs during tests
                            ["Backup:RetentionCount"] = retention.ToString(),
                            ["Backup:Local:Enabled"] = "true",
                            ["Backup:Local:Directory"] = _backupDir,
                        }
                    )
            )
        );

    private static HttpClient Management(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {GrimoireWebApplicationFactory.AdminKey}"
        );
        return client;
    }

    [Fact]
    public async Task Endpoints_Return409_WhenBackupsAreNotConfigured()
    {
        var client = factory.CreateManagementClient();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.GetAsync("/management/backups")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostAsync("/management/backups", null)).StatusCode
        );
    }

    [Fact]
    public async Task Endpoints_RequireAdminKey()
    {
        using var app = WithBackups();
        var response = await app.CreateClient().PostAsync("/management/backups", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_CreatesEncryptedBackup_ThatIsListed()
    {
        using var app = WithBackups();
        var client = Management(app);

        var response = await client.PostAsync("/management/backups", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var name = created["name"]!.GetValue<string>();
        Assert.True(BackupNaming.IsBackupName(name));
        Assert.Equal("local", created["targets"]![0]!.GetValue<string>());

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_backupDir, name));
        Assert.Equal("GRIMBAK\0"u8.ToArray(), bytes[..8]);
        Assert.Equal(bytes.Length, created["sizeBytes"]!.GetValue<long>());

        var list = JsonNode.Parse(await client.GetStringAsync("/management/backups"))!.AsArray();
        Assert.Equal(name, list[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Retention_KeepsOnlyTheNewestBackups()
    {
        using var app = WithBackups(retention: 2);
        var client = Management(app);

        var names = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsync("/management/backups", null);
            response.EnsureSuccessStatusCode();
            names.Add(
                JsonNode.Parse(await response.Content.ReadAsStringAsync())![
                    "name"
                ]!.GetValue<string>()
            );
            await Task.Delay(5); // distinct millisecond timestamps
        }

        var listed = JsonNode
            .Parse(await client.GetStringAsync("/management/backups"))!
            .AsArray()
            .Select(n => n!["name"]!.GetValue<string>())
            .ToList();
        Assert.Equal(names.AsEnumerable().Reverse().Take(2), listed);
    }

    [Fact]
    public async Task VerifyLatest_RestoresIntoThrowawayCopy_AndDecryptsASecret()
    {
        using var app = WithBackups();
        var client = Management(app);
        var (slug, _) = await TestHelpers.CreateApplicationAsync(client);
        await TestHelpers.CreateSecretWithValueAsync(client, slug, "DbPassword", "local", "s3cr3t");
        (await client.PostAsync("/management/backups", null)).EnsureSuccessStatusCode();

        var result = await app
            .Services.GetRequiredService<BackupRestorer>()
            .VerifyAsync(BackupRestorer.Latest, CancellationToken.None);

        Assert.True(result.Passed);
        Assert.True(result.Applications >= 1);
        Assert.True(result.SecretVersions >= 1);
        Assert.True(result.SecretDecrypted);
    }

    [Fact]
    public async Task Restore_ReplacesDatabase_KeepingThePreviousFile()
    {
        using var app = WithBackups();
        var client = Management(app);
        var (slug, _) = await TestHelpers.CreateApplicationAsync(client);
        (await client.PostAsync("/management/backups", null)).EnsureSuccessStatusCode();
        var backupFile = Directory.GetFiles(_backupDir).Single();

        var restoreDir = Path.Combine(_backupDir, "restore-target");
        var restoredDb = Path.Combine(restoreDir, "grimoire.db");
        var restorer = new BackupRestorer(
            app.Services.GetRequiredService<BackupOptions>(),
            [],
            app.Services.GetRequiredService<IEncryptionService>(),
            $"Data Source={restoredDb}"
        );

        await restorer.RestoreAsync(backupFile, force: false, CancellationToken.None);
        Assert.Equal(1, CountApplications(restoredDb, slug));

        // Existing database: refused without --force, kept aside with it.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restorer.RestoreAsync(backupFile, force: false, CancellationToken.None)
        );
        await restorer.RestoreAsync(backupFile, force: true, CancellationToken.None);
        Assert.Single(Directory.GetFiles(restoreDir, "grimoire.db.pre-restore-*"));
        Assert.Equal(1, CountApplications(restoredDb, slug));
    }

    [Fact]
    public async Task Verify_WithWrongBackupKey_Fails()
    {
        using var app = WithBackups();
        (await Management(app).PostAsync("/management/backups", null)).EnsureSuccessStatusCode();

        var restorer = new BackupRestorer(
            new BackupOptions { Key = "a-different-backup-key-of-32-chars!!" },
            app.Services.GetRequiredService<IReadOnlyList<IBackupTarget>>(),
            app.Services.GetRequiredService<IEncryptionService>(),
            "Data Source=unused.db"
        );

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            restorer.VerifyAsync(BackupRestorer.Latest, CancellationToken.None)
        );
    }

    private static long CountApplications(string dbPath, string slug)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Applications WHERE Slug = $slug";
        command.Parameters.AddWithValue("$slug", slug);
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_backupDir))
            Directory.Delete(_backupDir, recursive: true);
    }
}
