using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence;

/// <summary>
/// Creates or bumps the sentinel entries; the caller saves. Callers hold the database write
/// lock (a write transaction), so the committed value read here is the latest one and the next
/// value is always new, even when another save bumped the sentinel since this context loaded it.
/// </summary>
internal static class SentinelWriter
{
    private const string SentinelDescription =
        "Managed by Grimoire: changes when configuration is published, so clients know to reload.";

    public static async Task BumpApplicationAsync(
        GrimoireDbContext db,
        AppEnvironment env,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var committed = await db
            .ConfigurationEntries.AsNoTracking()
            .Where(e => e.EnvironmentId == env.Id && e.Key == ConfigurationSentinel.ApplicationKey)
            .Select(e => e.Value)
            .FirstOrDefaultAsync(ct);
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
                    Value = ConfigurationSentinel.NextValue(committed, now),
                    Description = SentinelDescription,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            return;
        }
        sentinel.Value = ConfigurationSentinel.NextValue(Latest(committed, sentinel.Value), now);
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
        var committed = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Where(e =>
                e.EnvironmentSlug == allEnvironments && e.Key == ConfigurationSentinel.GlobalKey
            )
            .Select(e => e.Value)
            .FirstOrDefaultAsync(ct);
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
                    Value = ConfigurationSentinel.NextValue(committed, now),
                    Description = SentinelDescription,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
            return;
        }
        sentinel.Value = ConfigurationSentinel.NextValue(Latest(committed, sentinel.Value), now);
        sentinel.ValueType = ConfigurationValueType.String;
        sentinel.UpdatedAt = now;
    }

    /// <summary>The larger of two sentinel values (a tracked copy can be stale or ahead).</summary>
    private static string? Latest(string? committed, string? tracked) =>
        long.TryParse(committed, out var a) && long.TryParse(tracked, out var b)
            ? Math.Max(a, b).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : committed ?? tracked;
}
