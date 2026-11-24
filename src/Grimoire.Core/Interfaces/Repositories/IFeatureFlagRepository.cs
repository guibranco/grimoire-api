using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IFeatureFlagRepository
{
    /// <summary>An application's flags (or, with <c>null</c>, the global flags) with their states, by key.</summary>
    Task<List<FeatureFlag>> ListAsync(Guid? applicationId, CancellationToken ct = default);

    /// <summary>
    /// Every global flag and every flag of an application that isn't deleted, and how many of
    /// them are on in at least one environment.
    /// </summary>
    Task<(int Total, int Enabled)> CountAsync(CancellationToken ct = default);

    /// <summary>One flag with its environment states (tracked).</summary>
    Task<FeatureFlag?> GetAsync(Guid? applicationId, string key, CancellationToken ct = default);

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
        Guid? applicationId,
        string key,
        CancellationToken ct = default
    );
}
