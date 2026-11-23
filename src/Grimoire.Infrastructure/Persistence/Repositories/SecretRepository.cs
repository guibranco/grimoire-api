using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class SecretRepository(GrimoireDbContext db) : ISecretRepository
{
    public Task<List<Secret>> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken ct = default
    ) => db.Secrets.Where(s => s.ApplicationId == applicationId).ToListAsync(ct);

    public Task<Secret?> GetByNameAsync(
        Guid applicationId,
        string name,
        CancellationToken ct = default
    ) =>
        db.Secrets.FirstOrDefaultAsync(s => s.ApplicationId == applicationId && s.Name == name, ct);

    public async Task AddAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Add(secret);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Remove(secret);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<SecretVersion>> GetVersionsAsync(
        Guid secretId,
        Guid environmentId,
        CancellationToken ct = default
    ) =>
        db
            .SecretVersions.Where(v => v.SecretId == secretId && v.EnvironmentId == environmentId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

    public async Task<SecretVersion?> GetActiveVersionAsync(
        Guid secretId,
        Guid environmentId,
        DateTimeOffset now,
        CancellationToken ct = default
    )
    {
        // Two-stage: filter indexable columns in SQL, then apply nullable DateTimeOffset comparisons in memory
        // (EF Core 10 SQLite provider cannot translate nullable DateTimeOffset OR expressions)
        var candidates = await db
            .SecretVersions.Where(v =>
                v.SecretId == secretId && v.EnvironmentId == environmentId && v.IsEnabled
            )
            .ToListAsync(ct);

        return SecretVersionRules.Current(candidates, now);
    }

    public async Task<int> GetNextVersionNumberAsync(
        Guid secretId,
        Guid environmentId,
        CancellationToken ct = default
    )
    {
        var max = await db
            .SecretVersions.Where(v => v.SecretId == secretId && v.EnvironmentId == environmentId)
            .MaxAsync(v => (int?)v.Version, ct);
        return (max ?? 0) + 1;
    }

    public Task<SecretVersion?> GetVersionAsync(
        Guid secretId,
        Guid environmentId,
        int version,
        CancellationToken ct = default
    ) =>
        db.SecretVersions.FirstOrDefaultAsync(
            v => v.SecretId == secretId && v.EnvironmentId == environmentId && v.Version == version,
            ct
        );

    public async Task AddVersionAsync(
        SecretVersion version,
        DateTimeOffset? expirePreviousAt = null,
        CancellationToken ct = default
    )
    {
        // Expiring the previous versions reads their committed expiry and writes a new one, so
        // it runs under the write lock: a concurrent PATCH can't slip an earlier expiry in
        // between and then be overwritten by the grace cap.
        await db.InWriteTransactionAsync(
            async token =>
            {
                if (expirePreviousAt is { } expiresAt)
                {
                    var others = await GetVersionsAsync(
                        version.SecretId,
                        version.EnvironmentId,
                        token
                    );
                    foreach (var previous in SecretVersionRules.VersionsToExpire(others, expiresAt))
                        previous.ExpiresAt = expiresAt;
                }
                db.SecretVersions.Add(version);
                return await db.SaveChangesAsync(token);
            },
            ct
        );
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<T> InWriteTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default
    ) => db.InWriteTransactionAsync(work, ct);
}
