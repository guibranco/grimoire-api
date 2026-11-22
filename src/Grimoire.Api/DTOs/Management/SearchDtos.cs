namespace Grimoire.Api.DTOs.Management;

/// <param name="Query">The trimmed search term.</param>
/// <param name="Results">Ranked hits: exact, then prefix, then contains, then description matches.</param>
/// <param name="Truncated"><c>true</c> when more hits exist than <c>limit</c>.</param>
public record SearchResponse(
    string Query,
    IReadOnlyList<SearchResultResponse> Results,
    bool Truncated
);

/// <param name="Type"><c>application</c>, <c>secret</c>, <c>configuration</c>, <c>global-secret</c> or <c>global-configuration</c>.</param>
/// <param name="Name">Application name, secret name or configuration key.</param>
/// <param name="Application">Application slug; <c>null</c> for global entries.</param>
/// <param name="Environments">Environment slugs holding a value (<c>*</c> = all, for globals).</param>
/// <param name="Description">Description of the entry, if any.</param>
/// <param name="ValueType">Configuration value type (<c>string</c>, <c>multiline</c>, <c>array</c>, <c>json</c>); configuration hits only.</param>
public record SearchResultResponse(
    string Type,
    string Name,
    string? Application,
    IReadOnlyList<string> Environments,
    string? Description,
    string? ValueType
);
