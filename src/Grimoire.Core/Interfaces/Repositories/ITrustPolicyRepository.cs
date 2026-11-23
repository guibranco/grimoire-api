using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface ITrustPolicyRepository
{
    /// <summary>The application's policies, by name.</summary>
    Task<List<TrustPolicy>> ListAsync(Guid applicationId, CancellationToken ct = default);

    Task<TrustPolicy?> GetAsync(Guid applicationId, Guid id, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid applicationId, Guid id, CancellationToken ct = default);

    /// <summary>Records a use, touching only <c>LastUsedAt</c>.</summary>
    Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken ct = default);

    Task<bool> NameExistsAsync(
        Guid applicationId,
        string name,
        Guid? exceptId = null,
        CancellationToken ct = default
    );

    Task AddAsync(TrustPolicy policy, CancellationToken ct = default);

    Task UpdateAsync(TrustPolicy policy, CancellationToken ct = default);

    Task DeleteAsync(TrustPolicy policy, CancellationToken ct = default);
}
