using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class ApplicationRepository(GrimoireDbContext db) : IApplicationRepository
{
    public Task<List<Application>> GetAllAsync(CancellationToken ct = default) =>
        db.Applications.Include(a => a.Environments).ToListAsync(ct);

    public Task<Application?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Applications.Include(a => a.Environments).FirstOrDefaultAsync(a => a.Slug == slug, ct);

    public Task<Application?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Application application, CancellationToken ct = default)
    {
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Application application, CancellationToken ct = default)
    {
        db.Applications.Update(application);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        db.Applications.AnyAsync(a => a.Slug == slug, ct);

    public Task<Application?> FindByApiKeyHashAsync(string hash, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(a => a.ApiKeyHash == hash, ct);
}
