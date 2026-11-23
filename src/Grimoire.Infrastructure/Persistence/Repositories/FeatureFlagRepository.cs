using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class FeatureFlagRepository(GrimoireDbContext db) : IFeatureFlagRepository
{
    public Task<List<FeatureFlag>> ListAsync(Guid applicationId, CancellationToken ct = default) =>
        db
            .FeatureFlags.Include(f => f.Environments)
            .Where(f => f.ApplicationId == applicationId)
            .OrderBy(f => f.Key)
            .ToListAsync(ct);

    public Task<FeatureFlag?> GetAsync(
        Guid applicationId,
        string key,
        CancellationToken ct = default
    ) =>
        db
            .FeatureFlags.Include(f => f.Environments)
            .FirstOrDefaultAsync(f => f.ApplicationId == applicationId && f.Key == key, ct);

    public async Task SaveAsync(
        FeatureFlag flag,
        FeatureFlagAuditEntry audit,
        CancellationToken ct = default
    )
    {
        if (db.Entry(flag).State == EntityState.Detached)
            db.FeatureFlags.Add(flag);
        db.FeatureFlagAuditEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(
        FeatureFlag flag,
        FeatureFlagAuditEntry audit,
        CancellationToken ct = default
    )
    {
        db.FeatureFlags.Remove(flag);
        db.FeatureFlagAuditEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    public Task<T> InWriteTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default
    ) => db.InWriteTransactionAsync(work, ct);

    public Task<List<FeatureFlagAuditEntry>> HistoryAsync(
        Guid applicationId,
        string key,
        CancellationToken ct = default
    ) =>
        db
            .FeatureFlagAuditEntries.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId && a.FlagKey == key)
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);
}
