using Grimoire.Core.Search;

namespace Grimoire.Core.Interfaces;

/// <summary>
/// Finds applications, secrets and configuration keys by name or description. Matches and
/// returns metadata only: values are never searched or returned.
/// </summary>
public interface IMetadataSearchService
{
    Task<SearchResult> SearchAsync(string query, int limit, CancellationToken ct = default);
}
