using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IGlobalSecretRepository
{
    Task<List<GlobalSecret>> GetAllAsync(CancellationToken ct = default);
    Task<GlobalSecret?> GetByNameAsync(string name, CancellationToken ct = default);
    Task AddAsync(GlobalSecret secret, CancellationToken ct = default);
    Task DeleteAsync(GlobalSecret secret, CancellationToken ct = default);

    Task<List<GlobalSecretVersion>> GetVersionsAsync(
        Guid globalSecretId,
        string environmentSlug,
        CancellationToken ct = default
    );

    /// <summary>
    /// Latest active version for <paramref name="environmentSlug"/>; if there is none,
    /// the latest active version for "*".
    /// </summary>
    Task<GlobalSecretVersion?> GetActiveVersionAsync(
        Guid globalSecretId,
        string environmentSlug,
        DateTimeOffset now,
        CancellationToken ct = default
    );

    Task<int> GetNextVersionNumberAsync(
        Guid globalSecretId,
        string environmentSlug,
        CancellationToken ct = default
    );
    Task AddVersionAsync(GlobalSecretVersion version, CancellationToken ct = default);
}
