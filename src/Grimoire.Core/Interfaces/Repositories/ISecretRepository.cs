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
    Task AddVersionAsync(SecretVersion version, CancellationToken ct = default);
}
