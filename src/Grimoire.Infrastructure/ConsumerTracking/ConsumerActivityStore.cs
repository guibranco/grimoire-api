using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Entity = Grimoire.Core.Entities.ConsumerActivity;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>Upserts buffered consumer reads into <c>ConsumerActivities</c>.</summary>
public sealed class ConsumerActivityStore(GrimoireDbContext db)
{
    public async Task SaveAsync(
        IReadOnlyList<PendingConsumerActivity> entries,
        CancellationToken ct
    )
    {
        if (entries.Count == 0)
            return;

        var environmentIds = entries.Select(e => e.EnvironmentId).Distinct().ToList();

        // The environment (or its application) may have been deleted since the read was recorded.
        var liveEnvironments = await db
            .Environments.Where(e => environmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.ApplicationId })
            .ToListAsync(ct);
        var live = liveEnvironments.Select(e => (e.ApplicationId, e.Id)).ToHashSet();

        var existing = await db
            .ConsumerActivities.Where(a => environmentIds.Contains(a.EnvironmentId))
            .ToDictionaryAsync(a => (a.ApplicationId, a.EnvironmentId), ct);

        foreach (var entry in entries)
        {
            var key = (entry.ApplicationId, entry.EnvironmentId);
            if (!live.Contains(key))
                continue;

            if (existing.TryGetValue(key, out var row))
            {
                if (entry.LastSeenAt >= row.LastSeenAt)
                {
                    row.LastSeenAt = entry.LastSeenAt;
                    row.LastUserAgent = entry.LastUserAgent;
                    row.LastIpAddress = entry.LastIpAddress;
                }
                row.RequestCount += entry.RequestCount;
            }
            else
            {
                db.ConsumerActivities.Add(
                    new Entity
                    {
                        ApplicationId = entry.ApplicationId,
                        EnvironmentId = entry.EnvironmentId,
                        LastSeenAt = entry.LastSeenAt,
                        LastUserAgent = entry.LastUserAgent,
                        LastIpAddress = entry.LastIpAddress,
                        RequestCount = entry.RequestCount,
                    }
                );
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
