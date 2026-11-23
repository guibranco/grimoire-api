using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class TrustPolicyRepository(GrimoireDbContext db) : ITrustPolicyRepository
{
    public Task<List<TrustPolicy>> ListAsync(Guid applicationId, CancellationToken ct = default) =>
        db
            .TrustPolicies.Where(p => p.ApplicationId == applicationId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public Task<TrustPolicy?> GetAsync(
        Guid applicationId,
        Guid id,
        CancellationToken ct = default
    ) =>
        db.TrustPolicies.FirstOrDefaultAsync(
            p => p.ApplicationId == applicationId && p.Id == id,
            ct
        );

    public Task<bool> NameExistsAsync(
        Guid applicationId,
        string name,
        Guid? exceptId = null,
        CancellationToken ct = default
    ) =>
        db.TrustPolicies.AnyAsync(
            p => p.ApplicationId == applicationId && p.Name == name && p.Id != exceptId,
            ct
        );

    public async Task AddAsync(TrustPolicy policy, CancellationToken ct = default)
    {
        db.TrustPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TrustPolicy policy, CancellationToken ct = default)
    {
        db.TrustPolicies.Update(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(TrustPolicy policy, CancellationToken ct = default)
    {
        db.TrustPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
    }
}
