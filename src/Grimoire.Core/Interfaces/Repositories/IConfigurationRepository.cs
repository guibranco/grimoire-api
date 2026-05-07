using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IConfigurationRepository
{
    Task<List<ConfigurationEntry>> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken ct = default
    );
    Task<List<ConfigurationEntry>> GetByEnvironmentAsync(
        Guid applicationId,
        Guid environmentId,
        CancellationToken ct = default
    );
    Task<ConfigurationEntry?> GetByKeyAsync(
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken ct = default
    );
    Task AddAsync(ConfigurationEntry entry, CancellationToken ct = default);
    Task UpdateAsync(ConfigurationEntry entry, CancellationToken ct = default);
    Task DeleteAsync(ConfigurationEntry entry, CancellationToken ct = default);
}
