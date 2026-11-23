using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class RotationReminderStore(GrimoireDbContext db) : IRotationReminderStore
{
    public async Task<IReadOnlySet<string>> GetSentAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    )
    {
        if (keys.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);
        var sent = await db
            .RotationReminders.AsNoTracking()
            .Where(r => keys.Contains(r.Key))
            .Select(r => r.Key)
            .ToListAsync(ct);
        return sent.ToHashSet(StringComparer.Ordinal);
    }

    public async Task MarkSentAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset sentAt,
        CancellationToken ct = default
    )
    {
        db.RotationReminders.AddRange(
            keys.Select(k => new RotationReminder
            {
                Id = Guid.NewGuid(),
                Key = k,
                SentAt = sentAt,
            })
        );
        await db.SaveChangesAsync(ct);
    }

    public Task<int> DeleteSentBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        db.RotationReminders.Where(r => r.SentAt < cutoff).ExecuteDeleteAsync(ct);
}
