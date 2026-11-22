using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface ISecretRepository
{
    Task<List<Secret>> GetByApplicationAsync(Guid applicationId, CancellationToken ct = default);
    Task<Secret?> GetByNameAsync(Guid applicationId, string name, CancellationToken ct = default);
    Task AddAsync(Secret secret, CancellationToken ct = default);
    Task DeleteAsync(Secret secret, CancellationToken ct = default);

    Task<List<SecretVersion>> GetVersionsAsync(
        Guid secretId,
        Guid environmentId,
        CancellationToken ct = default
    );
    Task<SecretVersion?> GetActiveVersionAsync(
        Guid secretId,
        Guid environmentId,
        DateTimeOffset now,
        CancellationToken ct = default
    );
    Task<int> GetNextVersionNumberAsync(
        Guid secretId,
        Guid environmentId,
        CancellationToken ct = default
    );
    Task<SecretVersion?> GetVersionAsync(
        Guid secretId,
        Guid environmentId,
        int version,
        CancellationToken ct = default
    );

    /// <summary>
    /// Adds <paramref name="version"/>. With <paramref name="expirePreviousAt"/>, every other
    /// enabled version of the same secret and environment with no expiry (or a later one) is
    /// given that expiry in the same save.
    /// </summary>
    Task AddVersionAsync(
        SecretVersion version,
        DateTimeOffset? expirePreviousAt = null,
        CancellationToken ct = default
    );

    /// <summary>Saves changes made to versions returned by this repository.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
