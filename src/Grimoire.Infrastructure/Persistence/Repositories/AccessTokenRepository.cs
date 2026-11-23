using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class AccessTokenRepository(GrimoireDbContext db) : IAccessTokenRepository
{
    public Task<List<AccessToken>> ListAsync(Guid applicationId, CancellationToken ct = default) =>
        db
            .AccessTokens.Where(t => t.ApplicationId == applicationId && t.TrustPolicyId == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<AccessToken?> GetAsync(
        Guid applicationId,
        Guid id,
        CancellationToken ct = default
    ) =>
        db.AccessTokens.FirstOrDefaultAsync(
            t => t.ApplicationId == applicationId && t.Id == id,
            ct
        );

    public Task<List<AccessToken>> FindByPrefixAsync(
        string prefix,
        CancellationToken ct = default
    ) => db.AccessTokens.Where(t => t.Prefix == prefix).ToListAsync(ct);

    public Task<bool> NameExistsAsync(
        Guid applicationId,
        string name,
        CancellationToken ct = default
    ) => db.AccessTokens.AnyAsync(t => t.ApplicationId == applicationId && t.Name == name, ct);

    public async Task AddAsync(AccessToken token, CancellationToken ct = default)
    {
        db.AccessTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AccessToken token, CancellationToken ct = default)
    {
        db.AccessTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }

    public Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken ct = default) =>
        db
            .AccessTokens.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, usedAt), ct);

    public Task<int> RevokeIssuedAsync(
        Guid trustPolicyId,
        DateTimeOffset at,
        CancellationToken ct = default
    ) =>
        db
            .AccessTokens.Where(t => t.TrustPolicyId == trustPolicyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, at), ct);

    public async Task<int> DeleteExpiredIssuedAsync(
        DateTimeOffset before,
        CancellationToken ct = default
    )
    {
        // ExpiresAt is stored as text, so compare in memory (issued tokens are few and short-lived).
        var issued = await db.AccessTokens.Where(t => t.TrustPolicyId != null).ToListAsync(ct);
        var expired = issued.Where(t => t.ExpiresAt is { } e && e < before).ToList();
        db.AccessTokens.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
