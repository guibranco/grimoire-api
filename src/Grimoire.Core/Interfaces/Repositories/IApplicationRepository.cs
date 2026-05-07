using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IApplicationRepository
{
    Task<List<Application>> GetAllAsync(CancellationToken ct = default);
    Task<Application?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Application?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Application application, CancellationToken ct = default);
    Task UpdateAsync(Application application, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<Application?> FindByApiKeyHashAsync(string hash, CancellationToken ct = default);
}
