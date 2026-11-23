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
    Task<GlobalSecretVersion?> GetVersionAsync(
        Guid globalSecretId,
        string environmentSlug,
        int version,
        CancellationToken ct = default
    );

    /// <inheritdoc cref="ISecretRepository.AddVersionAsync"/>
    Task AddVersionAsync(
        GlobalSecretVersion version,
        DateTimeOffset? expirePreviousAt = null,
        CancellationToken ct = default
    );

    /// <summary>Saves changes made to versions returned by this repository.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> inside a write transaction that holds the database write
    /// lock from its first read, so version reads, rule checks and saves made inside it can't
    /// interleave with another writer. Joins the ambient transaction when one is open.
    /// </summary>
    Task<T> InWriteTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default
    );
}
