using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class GlobalSecretRepository(GrimoireDbContext db) : IGlobalSecretRepository
{
    public Task<List<GlobalSecret>> GetAllAsync(CancellationToken ct = default) =>
        db.GlobalSecrets.OrderBy(s => s.Name).ToListAsync(ct);

    public Task<GlobalSecret?> GetByNameAsync(string name, CancellationToken ct = default) =>
        db.GlobalSecrets.FirstOrDefaultAsync(s => s.Name == name, ct);

    public async Task AddAsync(GlobalSecret secret, CancellationToken ct = default)
    {
        db.GlobalSecrets.Add(secret);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(GlobalSecret secret, CancellationToken ct = default)
    {
        db.GlobalSecrets.Remove(secret);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<GlobalSecretVersion>> GetVersionsAsync(
        Guid globalSecretId,
        string environmentSlug,
        CancellationToken ct = default
    ) =>
        db
            .GlobalSecretVersions.Where(v =>
                v.GlobalSecretId == globalSecretId && v.EnvironmentSlug == environmentSlug
            )
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

    public async Task<GlobalSecretVersion?> GetActiveVersionAsync(
        Guid globalSecretId,
        string environmentSlug,
        DateTimeOffset now,
        CancellationToken ct = default
    )
    {
        // Same two-stage approach as SecretRepository: filter indexable columns in SQL, then
        // apply nullable DateTimeOffset comparisons in memory (not translatable on SQLite).
        var candidates = await db
            .GlobalSecretVersions.Where(v =>
                v.GlobalSecretId == globalSecretId
                && v.IsEnabled
                && (
                    v.EnvironmentSlug == environmentSlug
                    || v.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments
                )
            )
            .ToListAsync(ct);

        var active = candidates.Where(v => SecretVersionRules.IsValid(v, now)).ToList();

        return active.Where(v => v.EnvironmentSlug == environmentSlug).MaxBy(v => v.Version)
            ?? active
                .Where(v => v.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments)
                .MaxBy(v => v.Version);
    }

    public async Task<int> GetNextVersionNumberAsync(
        Guid globalSecretId,
        string environmentSlug,
        CancellationToken ct = default
    )
    {
        var max = await db
            .GlobalSecretVersions.Where(v =>
                v.GlobalSecretId == globalSecretId && v.EnvironmentSlug == environmentSlug
            )
            .MaxAsync(v => (int?)v.Version, ct);
        return (max ?? 0) + 1;
    }

    public Task<GlobalSecretVersion?> GetVersionAsync(
        Guid globalSecretId,
        string environmentSlug,
        int version,
        CancellationToken ct = default
    ) =>
        db.GlobalSecretVersions.FirstOrDefaultAsync(
            v =>
                v.GlobalSecretId == globalSecretId
                && v.EnvironmentSlug == environmentSlug
                && v.Version == version,
            ct
        );

    public async Task AddVersionAsync(
        GlobalSecretVersion version,
        DateTimeOffset? expirePreviousAt = null,
        CancellationToken ct = default
    )
    {
        // See SecretRepository.AddVersionAsync: the grace expiry runs under the write lock.
        await db.InWriteTransactionAsync(
            async token =>
            {
                if (expirePreviousAt is { } expiresAt)
                {
                    var others = await GetVersionsAsync(
                        version.GlobalSecretId,
                        version.EnvironmentSlug,
                        token
                    );
                    foreach (var previous in SecretVersionRules.VersionsToExpire(others, expiresAt))
                        previous.ExpiresAt = expiresAt;
                }
                db.GlobalSecretVersions.Add(version);
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
