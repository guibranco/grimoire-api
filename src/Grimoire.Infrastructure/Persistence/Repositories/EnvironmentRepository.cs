using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class EnvironmentRepository(GrimoireDbContext db) : IEnvironmentRepository
{
    public Task<List<AppEnvironment>> GetByApplicationAsync(Guid applicationId, CancellationToken ct = default) =>
        db.Environments.Where(e => e.ApplicationId == applicationId).ToListAsync(ct);

    public Task<AppEnvironment?> GetBySlugAsync(Guid applicationId, string slug, CancellationToken ct = default) =>
        db.Environments.FirstOrDefaultAsync(e => e.ApplicationId == applicationId && e.Slug == slug, ct);

    public async Task AddAsync(AppEnvironment environment, CancellationToken ct = default)
    {
        db.Environments.Add(environment);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(AppEnvironment environment, CancellationToken ct = default)
    {
        db.Environments.Remove(environment);
        await db.SaveChangesAsync(ct);
    }
}
