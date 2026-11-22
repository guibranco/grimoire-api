using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>
/// Last-seen times from the database, overlaid with reads still buffered in memory, so a read
/// shows up immediately rather than after the next flush.
/// </summary>
public sealed class ConsumerActivityReader(GrimoireDbContext db, ConsumerActivityTracker tracker)
    : IConsumerActivityReader
{
    public async Task<IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    >?> GetLastSeenAsync(CancellationToken ct = default)
    {
        var stored = await db
            .ConsumerActivities.AsNoTracking()
            .Select(a => new
            {
                a.ApplicationId,
                a.EnvironmentId,
                a.LastSeenAt,
            })
            .ToListAsync(ct);

        var lastSeen = stored.ToDictionary(
            a => (a.ApplicationId, a.EnvironmentId),
            a => a.LastSeenAt
        );

        foreach (var (key, seen) in tracker.PendingLastSeen())
            if (!lastSeen.TryGetValue(key, out var current) || seen > current)
                lastSeen[key] = seen;

        return lastSeen;
    }
}
