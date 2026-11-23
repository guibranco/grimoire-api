using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Grimoire.IntegrationTests;

/// <summary>Upgrading seeds the secret read rollup from successful <c>secret.read</c> audit events.</summary>
public sealed class SecretReadMigrationTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"grimoire-migration-{Guid.NewGuid():N}.db"
    );

    private GrimoireDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options
        );

    private static AuditEvent Read(
        string app,
        string env,
        string target,
        DateTimeOffset at,
        AuditOutcome outcome = AuditOutcome.Success
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = at,
            Actor = $"app:{app}",
            Action = AuditActions.SecretRead,
            ApplicationSlug = app,
            Environment = env,
            TargetType = "secret",
            Target = target,
            Outcome = outcome,
            StatusCode = outcome == AuditOutcome.Success ? 200 : 404,
        };

    [Fact]
    public async Task SuccessfulAuditedReads_SeedTheRollup()
    {
        var appId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var globalId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("AddConfigurationRevisions");
            db.Applications.Add(
                new Application
                {
                    Id = appId,
                    Name = "Legacy",
                    Slug = "legacy",
                    ApiKeyHash = "x",
                    CreatedAt = Start,
                    UpdatedAt = Start,
                }
            );
            db.Environments.Add(
                new AppEnvironment
                {
                    Id = envId,
                    ApplicationId = appId,
                    Name = "Production",
                    Slug = "production",
                    CreatedAt = Start,
                    UpdatedAt = Start,
                }
            );
            db.Secrets.Add(
                new Secret
                {
                    Id = secretId,
                    ApplicationId = appId,
                    Name = "Db",
                    CreatedAt = Start,
                    UpdatedAt = Start,
                }
            );
            db.GlobalSecrets.Add(
                new GlobalSecret
                {
                    Id = globalId,
                    Name = "Smtp",
                    CreatedAt = Start,
                    UpdatedAt = Start,
                }
            );
            db.AuditEvents.AddRange(
                Read("legacy", "production", "Db", Start.AddDays(1)),
                Read("legacy", "production", "Db", Start.AddDays(3)),
                Read("legacy", "production", "Db", Start.AddDays(5), AuditOutcome.NotFound),
                Read("legacy", "production", "Smtp", Start.AddDays(2)),
                Read("legacy", "production", "Gone", Start.AddDays(2)),
                Read("other", "production", "Db", Start.AddDays(4))
            );
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var rows = await db.SecretReads.AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);

            var app = Assert.Single(rows, r => r.SecretId == secretId);
            Assert.Equal((appId, envId), (app.ApplicationId, app.EnvironmentId));
            Assert.Equal(Start.AddDays(3), app.LastReadAt);
            Assert.Equal(2, app.ReadCount);
            Assert.Null(app.GlobalSecretId);
            Assert.NotEqual(Guid.Empty, app.Id);

            var global = Assert.Single(rows, r => r.GlobalSecretId == globalId);
            Assert.Null(global.SecretId);
            Assert.Equal(Start.AddDays(2), global.LastReadAt);
            Assert.Equal(1, global.ReadCount);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            if (File.Exists(f))
                File.Delete(f);
    }
}
