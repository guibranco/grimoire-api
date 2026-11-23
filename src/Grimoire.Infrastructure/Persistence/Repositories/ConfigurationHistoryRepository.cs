using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence.Repositories;

public class ConfigurationHistoryRepository(GrimoireDbContext db) : IConfigurationHistoryRepository
{
    public Task<List<ConfigurationRevision>> GetHistoryAsync(
        ConfigurationScope scope,
        string environmentKey,
        string key,
        CancellationToken ct = default
    ) =>
        db
            .ConfigurationRevisions.AsNoTracking()
            .Where(r => r.Scope == scope && r.EnvironmentKey == environmentKey && r.Key == key)
            .OrderByDescending(r => r.Revision)
            .ToListAsync(ct);

    public Task<ConfigurationRevision?> GetRevisionAsync(
        ConfigurationScope scope,
        string environmentKey,
        string key,
        int revision,
        CancellationToken ct = default
    ) =>
        db
            .ConfigurationRevisions.AsNoTracking()
            .FirstOrDefaultAsync(
                r =>
                    r.Scope == scope
                    && r.EnvironmentKey == environmentKey
                    && r.Key == key
                    && r.Revision == revision,
                ct
            );
}
