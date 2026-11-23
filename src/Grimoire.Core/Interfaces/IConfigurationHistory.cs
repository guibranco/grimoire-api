using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces;

/// <summary>
/// Per-request context for configuration revisions: who is changing configuration, and whether
/// the change is a rollback. Revisions themselves are written automatically on save.
/// </summary>
public interface IConfigurationChangeContext
{
    string Actor { get; set; }

    /// <summary>Set before saving a rollback: the revision being restored.</summary>
    int? RollbackOf { get; set; }
}

public interface IConfigurationHistoryRepository
{
    /// <summary>Newest first.</summary>
    Task<List<ConfigurationRevision>> GetHistoryAsync(
        ConfigurationScope scope,
        string environmentKey,
        string key,
        CancellationToken ct = default
    );

    Task<ConfigurationRevision?> GetRevisionAsync(
        ConfigurationScope scope,
        string environmentKey,
        string key,
        int revision,
        CancellationToken ct = default
    );
}
