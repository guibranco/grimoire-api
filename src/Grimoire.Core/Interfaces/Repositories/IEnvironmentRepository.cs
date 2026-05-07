using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IEnvironmentRepository
{
    Task<List<AppEnvironment>> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken ct = default
    );
    Task<AppEnvironment?> GetBySlugAsync(
        Guid applicationId,
        string slug,
        CancellationToken ct = default
    );
    Task AddAsync(AppEnvironment environment, CancellationToken ct = default);
    Task DeleteAsync(AppEnvironment environment, CancellationToken ct = default);
}
