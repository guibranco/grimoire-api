using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Grimoire.IntegrationTests;

/// <summary>Upgrading a database with configuration seeds one "baseline" revision per entry.</summary>
public sealed class ConfigurationRevisionMigrationTests : IDisposable
{
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

    [Fact]
    public async Task ExistingEntries_GetABaselineRevision()
    {
        var appId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("AddRotationReminders");
            var now = DateTimeOffset.UtcNow;
            db.Applications.Add(
                new Application
                {
                    Id = appId,
                    Name = "Legacy",
                    Slug = "legacy",
                    ApiKeyHash = "x",
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            db.Environments.Add(
                new AppEnvironment
                {
                    Id = envId,
                    ApplicationId = appId,
                    Name = "Production",
                    Slug = "production",
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            db.ConfigurationEntries.Add(
                new ConfigurationEntry
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = appId,
                    EnvironmentId = envId,
                    Key = "Timeout",
                    Value = "30",
                    ValueType = ConfigurationValueType.Multiline,
                    Description = "seconds",
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            db.GlobalConfigurationEntries.Add(
                new GlobalConfigurationEntry
                {
                    Id = Guid.NewGuid(),
                    EnvironmentSlug = "*",
                    Key = "Region",
                    Value = "eu",
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var revisions = await db.ConfigurationRevisions.AsNoTracking().ToListAsync();

            var app = Assert.Single(revisions, r => r.Scope == ConfigurationScope.Application);
            Assert.Equal(appId, app.ApplicationId);
            Assert.Equal(ConfigurationRevision.EnvironmentKeyFor(envId), app.EnvironmentKey);
            Assert.Equal(
                ("Timeout", "30", 1, "baseline", "system"),
                (app.Key, app.Value, app.Revision, app.Change, app.ChangedBy)
            );
            Assert.Equal(ConfigurationValueType.Multiline, app.ValueType);
            Assert.Equal("seconds", app.Description);
            Assert.NotEqual(Guid.Empty, app.Id);

            var global = Assert.Single(revisions, r => r.Scope == ConfigurationScope.Global);
            Assert.Equal(("*", "Region", "eu"), (global.EnvironmentKey, global.Key, global.Value));
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
