namespace Grimoire.Core.Search;

/// <summary>
/// Orders search hits: exact match, then prefix, then contains on the name (or application slug),
/// then description-only matches. Ties go to applications first, then by type and name.
/// </summary>
public static class SearchRanker
{
    public const int MinQueryLength = 2;
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static SearchResult Rank(string query, IEnumerable<SearchHit> candidates, int limit)
    {
        var term = query.Trim();
        var ranked = candidates
            .Select(hit => (Hit: hit, Score: Score(term, hit)))
            .Where(x => x.Score is not null)
            .OrderBy(x => x.Score)
            .ThenBy(x => SearchResultTypes.Order(x.Hit.Type))
            .ThenBy(x => x.Hit.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Hit.Application, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Hit)
            .ToList();

        return new SearchResult(term, ranked.Take(limit).ToList(), ranked.Count > limit);
    }

    /// <returns>Lower is better; <c>null</c> when the hit doesn't match at all.</returns>
    internal static int? Score(string term, SearchHit hit)
    {
        var best = Rate(term, hit.Name);
        if (hit.Type == SearchResultTypes.Application && hit.Application is { } slug)
            best = Min(best, Rate(term, slug));
        if (best is not null)
            return best;

        return hit.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
            ? 3
            : null;
    }

    private static int? Rate(string term, string value) =>
        value.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0
        : value.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1
        : value.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2
        : null;

    private static int? Min(int? a, int? b) =>
        a is null ? b
        : b is null ? a
        : Math.Min(a.Value, b.Value);
}
