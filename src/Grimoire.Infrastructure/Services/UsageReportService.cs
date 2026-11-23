using Grimoire.Core.Interfaces;
using Grimoire.Core.Reports;
using Grimoire.Infrastructure.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// Reads the secret read rollup (plus reads still buffered in memory) and the current
/// applications, secrets and configuration keys. Deleted applications are left out.
/// </summary>
public sealed class UsageReportService(GrimoireDbContext db, SecretReadTracker pending)
    : IUsageReportService
{
    public async Task<IReadOnlyList<UnusedSecret>> ListUnusedSecretsAsync(
        DateTimeOffset since,
        CancellationToken ct = default
    )
    {
        var appSecrets = await (
            from s in db.Secrets.AsNoTracking()
            join a in db.Applications.AsNoTracking() on s.ApplicationId equals a.Id
            where !a.IsDeleted
            select new SecretUsageSubject(s.Id, false, a.Slug, a.Name, s.Name, s.CreatedAt)
        ).ToListAsync(ct);
        var globalSecrets = await db
            .GlobalSecrets.AsNoTracking()
            .Select(s => new SecretUsageSubject(s.Id, true, null, null, s.Name, s.CreatedAt))
            .ToListAsync(ct);

        return UnusedSecretsBuilder.Build(
            appSecrets.Concat(globalSecrets),
            await ReadSummariesAsync(ct),
            since
        );
    }

    public async Task<DependencyGraph> GetDependencyGraphAsync(CancellationToken ct = default)
    {
        var apps = await db
            .Applications.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Select(a => new GraphApplication(a.Id, a.Slug, a.Name, a.InheritGlobals))
            .ToListAsync(ct);
        var appSecrets = await db
            .Secrets.AsNoTracking()
            .Select(s => new GraphSecret(s.Id, s.ApplicationId, s.Name))
            .ToListAsync(ct);
        var globalSecrets = await db
            .GlobalSecrets.AsNoTracking()
            .Select(s => new GraphSecret(s.Id, null, s.Name))
            .ToListAsync(ct);
        var appConfig = await (
            from c in db.ConfigurationEntries.AsNoTracking()
            join e in db.Environments.AsNoTracking() on c.EnvironmentId equals e.Id
            select new GraphConfigEntry(c.ApplicationId, c.Key, e.Slug)
        ).ToListAsync(ct);
        var globalConfig = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Select(c => new GraphConfigEntry(null, c.Key, c.EnvironmentSlug))
            .ToListAsync(ct);

        return DependencyGraphBuilder.Build(
            new DependencyGraphInput(
                apps,
                [.. appSecrets, .. globalSecrets],
                [.. appConfig, .. globalConfig],
                await ReadSummariesAsync(ct)
            )
        );
    }

    /// <summary>Stored reads merged with those not flushed yet, one per environment and secret.</summary>
    private async Task<IReadOnlyList<SecretReadSummary>> ReadSummariesAsync(CancellationToken ct)
    {
        var environments = await (
            from e in db.Environments.AsNoTracking()
            join a in db.Applications.AsNoTracking() on e.ApplicationId equals a.Id
            where !a.IsDeleted
            select new
            {
                e.Id,
                EnvironmentSlug = e.Slug,
                ApplicationSlug = a.Slug,
            }
        ).ToDictionaryAsync(e => e.Id, ct);

        var stored = await db
            .SecretReads.AsNoTracking()
            .Select(r => new
            {
                r.EnvironmentId,
                IsGlobal = r.GlobalSecretId != null,
                SecretId = r.SecretId ?? r.GlobalSecretId!.Value,
                r.LastReadAt,
                r.ReadCount,
            })
            .ToListAsync(ct);

        var merged = new Dictionary<(Guid, bool, Guid), (DateTimeOffset LastReadAt, long Count)>();
        void Add(Guid envId, bool isGlobal, Guid secretId, DateTimeOffset at, long count)
        {
            var key = (envId, isGlobal, secretId);
            merged[key] = merged.TryGetValue(key, out var seen)
                ? (at > seen.LastReadAt ? at : seen.LastReadAt, seen.Count + count)
                : (at, count);
        }

        foreach (var r in stored)
            Add(r.EnvironmentId, r.IsGlobal, r.SecretId, r.LastReadAt, r.ReadCount);
        foreach (var r in pending.Pending())
            Add(
                r.EnvironmentId,
                r.Source == ValueSource.Global,
                r.SecretId,
                r.LastReadAt,
                r.ReadCount
            );

        return merged
            .Where(m => environments.ContainsKey(m.Key.Item1))
            .Select(m =>
            {
                var env = environments[m.Key.Item1];
                return new SecretReadSummary(
                    m.Key.Item3,
                    m.Key.Item2,
                    env.ApplicationSlug,
                    env.EnvironmentSlug,
                    m.Value.LastReadAt,
                    m.Value.Count
                );
            })
            .ToList();
    }
}
