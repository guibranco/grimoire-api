using Grimoire.Core.Dashboard;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// Reads value-free projections of every table the dashboard needs (one query per table, no
/// entity graphs, no secret or configuration values) and hands them to <see cref="DashboardBuilder"/>.
/// Aggregation happens in memory because SQLite cannot compare or aggregate
/// <see cref="DateTimeOffset"/> columns in SQL.
/// </summary>
public class DashboardService(
    GrimoireDbContext db,
    IConsumerActivityReader consumerActivity,
    IMemoryCache cache,
    IOptions<DashboardOptions> options,
    TimeProvider time
) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(
        int? expiringWithinDays = null,
        int? staleAfterDays = null,
        CancellationToken ct = default
    )
    {
        var settings = options.Value;
        var expiring = expiringWithinDays ?? settings.ExpiringWithinDays;
        var stale = staleAfterDays ?? settings.StaleAfterDays;

        if (settings.CacheSeconds <= 0)
            return await BuildAsync(expiring, stale, ct);

        var key = $"grimoire:dashboard:{expiring}:{stale}";
        if (cache.TryGetValue(key, out DashboardSummary? cached) && cached is not null)
            return cached;

        var summary = await BuildAsync(expiring, stale, ct);
        cache.Set(key, summary, TimeSpan.FromSeconds(settings.CacheSeconds));
        return summary;
    }

    private async Task<DashboardSummary> BuildAsync(
        int expiringWithinDays,
        int staleAfterDays,
        CancellationToken ct
    )
    {
        var snapshot = await LoadSnapshotAsync(ct);
        return DashboardBuilder.Build(
            snapshot,
            time.GetUtcNow(),
            expiringWithinDays,
            staleAfterDays
        );
    }

    private async Task<DashboardSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        // Applications carry a global query filter on IsDeleted; every other query joins through
        // the application so rows of soft-deleted applications are excluded too.
        var applications = await db
            .Applications.AsNoTracking()
            .Select(a => new ApplicationRow(a.Id, a.Slug, a.Name, a.InheritGlobals, a.UpdatedAt))
            .ToListAsync(ct);

        var environments = await db
            .Environments.AsNoTracking()
            .Where(e => !e.Application.IsDeleted)
            .Select(e => new EnvironmentRow(e.Id, e.ApplicationId, e.Slug, e.Name, e.UpdatedAt))
            .ToListAsync(ct);

        var secrets = await db
            .Secrets.AsNoTracking()
            .Where(s => !s.Application.IsDeleted)
            .Select(s => new SecretRow(s.Id, s.ApplicationId, s.Name, s.UpdatedAt))
            .ToListAsync(ct);

        var secretVersions = await db
            .SecretVersions.AsNoTracking()
            .Where(v => !v.Secret.Application.IsDeleted)
            .Select(v => new SecretVersionRow(
                v.SecretId,
                v.EnvironmentId,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.Version,
                v.CreatedAt
            ))
            .ToListAsync(ct);

        var configurations = await db
            .ConfigurationEntries.AsNoTracking()
            .Where(c => !c.Application.IsDeleted)
            .Select(c => new ConfigurationRow(
                c.ApplicationId,
                c.EnvironmentId,
                c.Key,
                string.IsNullOrWhiteSpace(c.Value),
                c.UpdatedAt
            ))
            .ToListAsync(ct);

        var globalSecrets = await db
            .GlobalSecrets.AsNoTracking()
            .Select(s => new GlobalSecretRow(s.Id, s.Name, s.UpdatedAt))
            .ToListAsync(ct);

        var globalSecretVersions = await db
            .GlobalSecretVersions.AsNoTracking()
            .Select(v => new GlobalSecretVersionRow(
                v.GlobalSecretId,
                v.EnvironmentSlug,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.Version,
                v.CreatedAt
            ))
            .ToListAsync(ct);

        var globalConfigurations = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Select(c => new GlobalConfigurationRow(
                c.EnvironmentSlug,
                c.Key,
                string.IsNullOrWhiteSpace(c.Value),
                c.UpdatedAt
            ))
            .ToListAsync(ct);

        var lastSeen = await consumerActivity.GetLastSeenAsync(ct);

        return new DashboardSnapshot(
            applications,
            environments,
            secrets,
            secretVersions,
            configurations,
            globalSecrets,
            globalSecretVersions,
            globalConfigurations,
            lastSeen
        );
    }
}

/// <summary>
/// Placeholder until consumer last-seen tracking (#103) lands: reports that activity is not
/// tracked, so <c>lastSeenAt</c> stays <c>null</c> and the consumer rules are skipped.
/// </summary>
public sealed class UntrackedConsumerActivityReader : IConsumerActivityReader
{
    public Task<IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    >?> GetLastSeenAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<(Guid, Guid), DateTimeOffset>?>(null);
}
