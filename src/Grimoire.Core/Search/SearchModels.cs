namespace Grimoire.Core.Search;

/// <summary>Result types, in tie-break order (applications first).</summary>
public static class SearchResultTypes
{
    public const string Application = "application";
    public const string Secret = "secret";
    public const string Configuration = "configuration";
    public const string GlobalSecret = "global-secret";
    public const string GlobalConfiguration = "global-configuration";

    internal static int Order(string type) =>
        type switch
        {
            Application => 0,
            Secret => 1,
            Configuration => 2,
            GlobalSecret => 3,
            GlobalConfiguration => 4,
            _ => 5,
        };
}

/// <summary>
/// A search hit: where a name or key lives. Never carries a value.
/// </summary>
/// <param name="Type">One of <see cref="SearchResultTypes"/>.</param>
/// <param name="Name">Application name, secret name or configuration key.</param>
/// <param name="Application">Application slug; <c>null</c> for global entries.</param>
/// <param name="Environments">
/// Environment slugs the secret or key has a value in (<c>*</c> = every environment, for globals).
/// Empty for applications and for secrets with no value yet.
/// </param>
/// <param name="Description">Optional description of the entry.</param>
/// <param name="ValueType">Configuration value type name; <c>null</c> for non-configuration hits.</param>
public sealed record SearchHit(
    string Type,
    string Name,
    string? Application,
    IReadOnlyList<string> Environments,
    string? Description = null,
    string? ValueType = null
);

public sealed record SearchResult(string Query, IReadOnlyList<SearchHit> Results, bool Truncated);
