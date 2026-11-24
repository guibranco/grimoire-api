using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence;

/// <summary>Creates or bumps the sentinel entries; the caller saves.</summary>
internal static class SentinelWriter
{
    public static async Task BumpApplicationAsync(
        GrimoireDbContext db,
        AppEnvironment env,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var sentinel = db.ConfigurationEntries.Local.FirstOrDefault(e =>
            e.EnvironmentId == env.Id && e.Key == ConfigurationSentinel.ApplicationKey
        )
            ?? await db.ConfigurationEntries.FirstOrDefaultAsync(
                e => e.EnvironmentId == env.Id && e.Key == ConfigurationSentinel.ApplicationKey,
                ct
            );
        if (sentinel is null)
        {
            db.ConfigurationEntries.Add(
                new ConfigurationEntry
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = env.ApplicationId,
                    EnvironmentId = env.Id,
                    Key = ConfigurationSentinel.ApplicationKey,
                    Value = ConfigurationSentinel.NextValue(null, now),
                    Description = SentinelDescription,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            return;
        }
        sentinel.Value = ConfigurationSentinel.NextValue(sentinel.Value, now);
        sentinel.ValueType = ConfigurationValueType.String;
        sentinel.UpdatedAt = now;
    }

    public static async Task BumpGlobalAsync(
        GrimoireDbContext db,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        const string allEnvironments = GlobalConfigurationEntry.AllEnvironments;
        var sentinel = db.GlobalConfigurationEntries.Local.FirstOrDefault(e =>
            e.EnvironmentSlug == allEnvironments && e.Key == ConfigurationSentinel.GlobalKey
        )
            ?? await db.GlobalConfigurationEntries.FirstOrDefaultAsync(
                e =>
                    e.EnvironmentSlug == allEnvironments
                    && e.Key == ConfigurationSentinel.GlobalKey,
                ct
            );
        if (sentinel is null)
        {
            db.GlobalConfigurationEntries.Add(
                new GlobalConfigurationEntry
                {
                    Id = Guid.NewGuid(),
                    EnvironmentSlug = allEnvironments,
                    Key = ConfigurationSentinel.GlobalKey,
                    Value = ConfigurationSentinel.NextValue(null, now),
                    Description = SentinelDescription,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            return;
        }
        sentinel.Value = ConfigurationSentinel.NextValue(sentinel.Value, now);
        sentinel.ValueType = ConfigurationValueType.String;
        sentinel.UpdatedAt = now;
    }

    private const string SentinelDescription =
        "Managed by Grimoire: changes when configuration is published, so clients know to reload.";
}
