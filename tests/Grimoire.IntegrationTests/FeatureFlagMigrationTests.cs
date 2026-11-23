using Grimoire.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Grimoire.IntegrationTests;

/// <summary>Upgrading to global flags keeps existing application flags, states and history.</summary>
public sealed class FeatureFlagMigrationTests : IDisposable
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
    public async Task ApplicationFlags_SurviveTheUpgrade()
    {
        var appId = Guid.NewGuid().ToString().ToUpperInvariant();
        var envId = Guid.NewGuid().ToString().ToUpperInvariant();
        var flagId = Guid.NewGuid().ToString().ToUpperInvariant();
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("AddFeatureFlags");
            const string now = "2026-11-23 09:00:00+00:00";
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $$"""
                INSERT INTO "Applications" ("Id","Name","Slug","Description","ApiKeyHash","IsDeleted","InheritGlobals","CreatedAt","UpdatedAt")
                VALUES ('{{appId}}','Legacy','legacy',NULL,'x',0,1,'{{now}}','{{now}}');
                INSERT INTO "Environments" ("Id","ApplicationId","Name","Slug","CreatedAt","UpdatedAt")
                VALUES ('{{envId}}','{{appId}}','Production','production','{{now}}','{{now}}');
                INSERT INTO "FeatureFlags" ("Id","ApplicationId","Key","Description","Type","Variants","DefaultVariant","Tags","Salt","CreatedAt","UpdatedAt")
                VALUES ('{{flagId}}','{{appId}}','beta',NULL,'boolean','[{"key":"on","value":"true"},{"key":"off","value":"false"}]','off','["x"]','0123456789abcdef','{{now}}','{{now}}');
                INSERT INTO "FeatureFlagEnvironmentStates" ("Id","FeatureFlagId","EnvironmentId","Enabled","OffVariant","Rules","Fallthrough","Version","UpdatedAt")
                VALUES ('{{Guid.NewGuid().ToString().ToUpperInvariant()}}','{{flagId}}','{{envId}}',1,NULL,'[]','{"variant":"on"}',3,'{{now}}');
                INSERT INTO "FeatureFlagAuditEntries" ("Id","ApplicationId","FlagKey","Environment","Change","Actor","ChangedAt","Before","After")
                VALUES ('{{Guid.NewGuid().ToString().ToUpperInvariant()}}','{{appId}}','beta',NULL,'create','admin',0,NULL,NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var flag = await db.FeatureFlags.Include(f => f.Environments).SingleAsync();
            Assert.Equal(Guid.Parse(appId), flag.ApplicationId);
            Assert.False(flag.IsGlobal);
            var state = Assert.Single(flag.Environments);
            Assert.Equal((Guid?)Guid.Parse(envId), state.EnvironmentId);
            Assert.Null(state.EnvironmentSlug);
            Assert.Equal(
                (true, 3, "on"),
                (state.Enabled, state.Version, state.Fallthrough!.Variant)
            );
            Assert.Equal(
                Guid.Parse(appId),
                (await db.FeatureFlagAuditEntries.SingleAsync()).ApplicationId
            );
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
