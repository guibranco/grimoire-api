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

    [Fact]
    public async Task CommandLine_Backup_Then_VerifyLatest_Succeed()
    {
        using var app = WithBackups();
        var client = Management(app);
        var (slug, _) = await TestHelpers.CreateApplicationAsync(client);
        await TestHelpers.CreateSecretWithValueAsync(client, slug, "Token", "local", "abc");

        Assert.Equal(
            0,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Backup, null, false),
                app.Services
            )
        );
        Assert.Equal(
            0,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Verify, BackupRestorer.Latest, false),
                app.Services
            )
        );
    }

    [Fact]
    public async Task CommandLine_Failures_ReturnOne()
    {
        // Not configured → --backup fails cleanly.
        Assert.Equal(
            1,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Backup, null, false),
                factory.Services
            )
        );

        using var app = WithBackups();
        // Missing file → verify and restore fail without touching the live database.
        var missing = Path.Combine(_backupDir, "missing.gbak");
        Assert.Equal(
            1,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Verify, missing, false),
                app.Services
            )
        );
        Assert.Equal(
            1,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Restore, missing, true),
                app.Services
            )
        );
    }

    [Fact]
    public async Task CommandLine_VerifyEmptyDatabaseBackup_FailsSmokeTest()
    {
        // A fresh database has no applications or secrets: the restore test must not pass.
        var emptyDb = Path.Combine(_backupDir, "empty.db");
        Directory.CreateDirectory(_backupDir);
        using (var connection = new SqliteConnection($"Data Source={emptyDb};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE __EFMigrationsHistory (MigrationId TEXT);
                CREATE TABLE Applications (Id TEXT, IsDeleted INTEGER);
                CREATE TABLE SecretVersions (EncryptedValue TEXT);
                CREATE TABLE GlobalSecretVersions (EncryptedValue TEXT);
                CREATE TABLE ConfigurationEntries (Id TEXT);
                """;
            command.ExecuteNonQuery();
        }
        var file = Path.Combine(_backupDir, "empty.gbak");
        await File.WriteAllBytesAsync(
            file,
            new BackupCipher(BackupKey).Encrypt(await File.ReadAllBytesAsync(emptyDb))
        );

        using var app = WithBackups();
        Assert.Equal(
            1,
            await Grimoire.Api.Backups.BackupCommandLine.RunAsync(
                new(Grimoire.Api.Backups.BackupCommandKind.Verify, file, false),
                app.Services
            )
        );
    }

    [Fact]
    public async Task Restore_RejectsFileThatIsNotAGrimoireDatabase()
    {
        var plain = Path.Combine(_backupDir, "plain.db");
        Directory.CreateDirectory(_backupDir);
        using (var connection = new SqliteConnection($"Data Source={plain};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Something (Id TEXT);";
            command.ExecuteNonQuery();
        }
        var file = Path.Combine(_backupDir, "plain.gbak");
        await File.WriteAllBytesAsync(
            file,
            new BackupCipher(BackupKey).Encrypt(await File.ReadAllBytesAsync(plain))
        );

        var target = Path.Combine(_backupDir, "target", "grimoire.db");
        var restorer = new BackupRestorer(
            new BackupOptions { Key = BackupKey },
            [],
            factory.Services.GetRequiredService<IEncryptionService>(),
            $"Data Source={target}"
        );

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            restorer.RestoreAsync(file, force: false, CancellationToken.None)
        );
        Assert.False(File.Exists(target));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restorer.LoadAsync(BackupRestorer.Latest, CancellationToken.None)
        );
    }

    /// <summary>Runs the real entry point (Grimoire.Api.dll) with the given args and environment.</summary>
    private static async Task<(int ExitCode, string Output)> RunApiAsync(
        IDictionary<string, string> environment,
        params string[] args
    )
    {
        var dll = typeof(Program).Assembly.Location;
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        start.ArgumentList.Add(dll);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["LogStream__Enabled"] = "false";
        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        return (process.ExitCode, await stdout + await stderr);
    }

    [Fact]
    public async Task EntryPoint_BackupRefusesSharedKey_ButVerifyStillWorks()
    {
        using var app = WithBackups();
        var client = Management(app);
        var (slug, _) = await TestHelpers.CreateApplicationAsync(client);
        await TestHelpers.CreateSecretWithValueAsync(client, slug, "Pwd", "local", "x");
        (await client.PostAsync("/management/backups", null)).EnsureSuccessStatusCode();
        var backupFile = Directory.GetFiles(_backupDir, "*.gbak").Single();

        var environment = new Dictionary<string, string>
        {
            ["ConnectionStrings__Default"] = $"Data Source={Path.Combine(_backupDir, "cli.db")}",
            ["Management__AdminApiKey"] = GrimoireWebApplicationFactory.AdminKey,
            ["Encryption__MasterKey"] = GrimoireWebApplicationFactory.MasterKey,
            ["Backup__Enabled"] = "true",
            ["Backup__Key"] = BackupKey,
            ["Backup__Local__Enabled"] = "true",
            ["Backup__Local__Directory"] = _backupDir,
            // An invalid creation setting must not block reading existing backups.
            ["Backup__RetentionCount"] = "0",
        };

        var verify = await RunApiAsync(environment, "--verify-backup", backupFile);
        Assert.True(verify.ExitCode == 0, verify.Output);
        Assert.Contains("Restore test passed", verify.Output);

        environment["Backup__RetentionCount"] = "5";
        environment["Backup__Key"] = GrimoireWebApplicationFactory.MasterKey;
        var backup = await RunApiAsync(environment, "--backup");
        Assert.Equal(2, backup.ExitCode);
        Assert.Contains("must differ from 'Encryption:MasterKey'", backup.Output);
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
