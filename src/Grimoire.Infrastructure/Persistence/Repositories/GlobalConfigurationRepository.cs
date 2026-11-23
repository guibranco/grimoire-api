using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class GlobalConfigurationRepository(GrimoireDbContext db) : IGlobalConfigurationRepository
{
    public Task<List<GlobalConfigurationEntry>> GetAllAsync(
        string? environmentSlug = null,
        CancellationToken ct = default
    )
    {
        var query = db.GlobalConfigurationEntries.AsQueryable();
        if (!string.IsNullOrEmpty(environmentSlug))
            query = query.Where(c => c.EnvironmentSlug == environmentSlug);
        return query.OrderBy(c => c.EnvironmentSlug).ThenBy(c => c.Key).ToListAsync(ct);
    }

    public Task<List<GlobalConfigurationEntry>> GetApplicableAsync(
        string environmentSlug,
        CancellationToken ct = default
    ) =>
        db
            .GlobalConfigurationEntries.Where(c =>
                c.EnvironmentSlug == environmentSlug
                || c.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments
            )
            .ToListAsync(ct);

    public Task<GlobalConfigurationEntry?> GetByKeyAsync(
        string environmentSlug,
        string key,
        CancellationToken ct = default
    ) =>
        db.GlobalConfigurationEntries.FirstOrDefaultAsync(
            c => c.EnvironmentSlug == environmentSlug && c.Key == key,
            ct
        );

    public async Task AddAsync(GlobalConfigurationEntry entry, CancellationToken ct = default)
    {
        db.GlobalConfigurationEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GlobalConfigurationEntry entry, CancellationToken ct = default)
    {
        // Entries loaded through this context are tracked: let change detection decide what
        // changed (Update() would mark every column modified and record an empty revision).
        if (db.Entry(entry).State == EntityState.Detached)
            db.GlobalConfigurationEntries.Update(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(GlobalConfigurationEntry entry, CancellationToken ct = default)
    {
        db.GlobalConfigurationEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }
}
