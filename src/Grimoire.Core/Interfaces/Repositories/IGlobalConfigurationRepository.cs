using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IGlobalConfigurationRepository
{
    /// <summary>Lists global entries, optionally filtered to an exact environment slug (including "*").</summary>
    Task<List<GlobalConfigurationEntry>> GetAllAsync(
        string? environmentSlug = null,
        CancellationToken ct = default
    );

    /// <summary>Entries that apply to <paramref name="environmentSlug"/>: those targeting it plus those targeting "*".</summary>
    Task<List<GlobalConfigurationEntry>> GetApplicableAsync(
        string environmentSlug,
        CancellationToken ct = default
    );

    Task<GlobalConfigurationEntry?> GetByKeyAsync(
        string environmentSlug,
        string key,
        CancellationToken ct = default
    );
    Task AddAsync(GlobalConfigurationEntry entry, CancellationToken ct = default);
    Task UpdateAsync(GlobalConfigurationEntry entry, CancellationToken ct = default);
    Task DeleteAsync(GlobalConfigurationEntry entry, CancellationToken ct = default);
}
