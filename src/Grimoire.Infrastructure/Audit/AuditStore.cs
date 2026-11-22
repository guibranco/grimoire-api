using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Audit;

/// <summary>Writes audit batches and applies retention.</summary>
public sealed class AuditStore(GrimoireDbContext db)
{
    public async Task SaveAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
            return;
        db.AuditEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
    }

    /// <returns>Number of events deleted.</returns>
    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var utc = cutoff.ToUniversalTime();
        return db.AuditEvents.Where(e => e.OccurredAt < utc).ExecuteDeleteAsync(ct);
    }
}
