using Grimoire.Core.Dashboard;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Publishing;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// Reads value-free projections of every table the dashboard needs (one query per table, no
/// entity graphs, no secret or configuration values; secrets via <see cref="SecretMetadataReader"/>) and hands them to <see cref="DashboardBuilder"/>.
/// Aggregation happens in memory because SQLite cannot compare or aggregate
/// <see cref="DateTimeOffset"/> columns in SQL.
/// </summary>
public class DashboardService(
    GrimoireDbContext db,
    SecretMetadataReader metadata,
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
        var secrets = await metadata.LoadAsync(ct);

        var configurations = await db
            .ConfigurationEntries.AsNoTracking()
            .Where(c => !c.Application.IsDeleted && c.Key != ConfigurationSentinel.ApplicationKey)
            .Select(c => new ConfigurationRow(
                c.ApplicationId,
                c.EnvironmentId,
                c.Key,
                string.IsNullOrWhiteSpace(c.Value),
                c.UpdatedAt
            ))
            .ToListAsync(ct);

        var globalConfigurations = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Where(c => c.Key != ConfigurationSentinel.GlobalKey)
            .Select(c => new GlobalConfigurationRow(
                c.EnvironmentSlug,
                c.Key,
                string.IsNullOrWhiteSpace(c.Value),
                c.UpdatedAt
            ))
            .ToListAsync(ct);

        var lastSeen = await consumerActivity.GetLastSeenAsync(ct);

        return new DashboardSnapshot(
            secrets.Applications,
            secrets.Environments,
            secrets.Secrets,
            secrets.SecretVersions,
            configurations,
            secrets.GlobalSecrets,
            secrets.GlobalSecretVersions,
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
