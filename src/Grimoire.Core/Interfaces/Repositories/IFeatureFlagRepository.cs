using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IFeatureFlagRepository
{
    /// <summary>The application's flags with their environment states, by key.</summary>
    Task<List<FeatureFlag>> ListAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>One flag with its environment states (tracked).</summary>
    Task<FeatureFlag?> GetAsync(Guid applicationId, string key, CancellationToken ct = default);

    /// <summary>
    /// Adds the flag (or saves changes to tracked flags) and appends <paramref name="audit"/> in the
    /// same transaction.
    /// </summary>
    Task SaveAsync(FeatureFlag flag, FeatureFlagAuditEntry audit, CancellationToken ct = default);

    Task DeleteAsync(FeatureFlag flag, FeatureFlagAuditEntry audit, CancellationToken ct = default);

    /// <summary>Runs <paramref name="work"/> holding the database write lock (read-check-write).</summary>
    Task<T> InWriteTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default
    );

    /// <summary>Change history for a key, newest first (survives delete and re-create).</summary>
    Task<List<FeatureFlagAuditEntry>> HistoryAsync(
        Guid applicationId,
        string key,
        CancellationToken ct = default
    );
}
