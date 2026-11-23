using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>Upserts buffered secret reads into <c>SecretReads</c>.</summary>
public sealed class SecretReadStore(GrimoireDbContext db)
{
    public async Task SaveAsync(IReadOnlyList<PendingSecretRead> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
            return;

        var environmentIds = entries.Select(e => e.EnvironmentId).Distinct().ToList();
        var appSecretIds = IdsFor(entries, ValueSource.Application);
        var globalSecretIds = IdsFor(entries, ValueSource.Global);

        // The environment or the secret may have been deleted since the read was recorded.
        var liveEnvironments = (
            await db
                .Environments.Where(e => environmentIds.Contains(e.Id))
                .Select(e => new { e.Id, e.ApplicationId })
                .ToListAsync(ct)
        )
            .Select(e => (e.ApplicationId, e.Id))
            .ToHashSet();
        var liveAppSecrets = (
            await db
                .Secrets.Where(s => appSecretIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct)
        ).ToHashSet();
        var liveGlobalSecrets = (
            await db
                .GlobalSecrets.Where(s => globalSecretIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct)
        ).ToHashSet();

        var existing = (
            await db
                .SecretReads.Where(r => environmentIds.Contains(r.EnvironmentId))
                .ToListAsync(ct)
        ).ToDictionary(r => (r.EnvironmentId, r.SecretId, r.GlobalSecretId));

        foreach (var entry in entries)
        {
            var isGlobal = entry.Source == ValueSource.Global;
            if (
                !liveEnvironments.Contains((entry.ApplicationId, entry.EnvironmentId))
                || !(isGlobal ? liveGlobalSecrets : liveAppSecrets).Contains(entry.SecretId)
            )
                continue;

            Guid? secretId = isGlobal ? null : entry.SecretId;
            Guid? globalSecretId = isGlobal ? entry.SecretId : null;
            if (existing.TryGetValue((entry.EnvironmentId, secretId, globalSecretId), out var row))
            {
                if (entry.LastReadAt > row.LastReadAt)
                    row.LastReadAt = entry.LastReadAt;
                row.ReadCount += entry.ReadCount;
                continue;
            }

            row = new SecretRead
            {
                Id = Guid.NewGuid(),
                ApplicationId = entry.ApplicationId,
                EnvironmentId = entry.EnvironmentId,
                SecretId = secretId,
                GlobalSecretId = globalSecretId,
                LastReadAt = entry.LastReadAt,
                ReadCount = entry.ReadCount,
            };
            db.SecretReads.Add(row);
            existing[(entry.EnvironmentId, secretId, globalSecretId)] = row;
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<Guid> IdsFor(IEnumerable<PendingSecretRead> entries, ValueSource source) =>
        entries.Where(e => e.Source == source).Select(e => e.SecretId).Distinct().ToList();
}
