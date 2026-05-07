using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class ConfigurationRepository(GrimoireDbContext db) : IConfigurationRepository
{
    public Task<List<ConfigurationEntry>> GetByApplicationAsync(Guid applicationId, CancellationToken ct = default) =>
        db.ConfigurationEntries
            .Include(c => c.Environment)
            .Where(c => c.ApplicationId == applicationId)
            .ToListAsync(ct);

    public Task<List<ConfigurationEntry>> GetByEnvironmentAsync(Guid applicationId, Guid environmentId, CancellationToken ct = default) =>
        db.ConfigurationEntries
            .Where(c => c.ApplicationId == applicationId && c.EnvironmentId == environmentId)
            .ToListAsync(ct);

    public Task<ConfigurationEntry?> GetByKeyAsync(Guid applicationId, Guid environmentId, string key, CancellationToken ct = default) =>
        db.ConfigurationEntries
            .FirstOrDefaultAsync(c => c.ApplicationId == applicationId && c.EnvironmentId == environmentId && c.Key == key, ct);

    public async Task AddAsync(ConfigurationEntry entry, CancellationToken ct = default)
    {
        db.ConfigurationEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ConfigurationEntry entry, CancellationToken ct = default)
    {
        db.ConfigurationEntries.Update(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ConfigurationEntry entry, CancellationToken ct = default)
    {
        db.ConfigurationEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }
}
