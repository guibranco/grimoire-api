using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Search;
using Grimoire.Core.Services;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// <c>LIKE</c> queries (case-insensitive for ASCII in SQLite) on names, keys and descriptions,
/// grouped per secret / key, then ranked by <see cref="SearchRanker"/>. Every match is ranked
/// before the limit is applied, so an exact match is never cut off and environment groups are
/// complete. Value columns are never part of a predicate or a projection.
/// </summary>
public sealed class MetadataSearchService(GrimoireDbContext db) : IMetadataSearchService
{
    private const string Escape = "\\";

    public async Task<SearchResult> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default
    )
    {
        var pattern = $"%{EscapeLike(query.Trim())}%";
        var hits = new List<SearchHit>();

        hits.AddRange(await ApplicationsAsync(pattern, ct));
        hits.AddRange(await SecretsAsync(pattern, ct));
        hits.AddRange(await ConfigurationsAsync(pattern, query.Trim(), ct));
        hits.AddRange(await GlobalSecretsAsync(pattern, ct));
        hits.AddRange(await GlobalConfigurationsAsync(pattern, query.Trim(), ct));

        return SearchRanker.Rank(query, hits, limit);
    }

    private async Task<IEnumerable<SearchHit>> ApplicationsAsync(
        string pattern,
        CancellationToken ct
    )
    {
        var apps = await db
            .Applications.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Where(a =>
                EF.Functions.Like(a.Name, pattern, Escape)
                || EF.Functions.Like(a.Slug, pattern, Escape)
                || (a.Description != null && EF.Functions.Like(a.Description, pattern, Escape))
            )
            .Select(a => new
            {
                a.Name,
                a.Slug,
                a.Description,
            })
            .ToListAsync(ct);

        return apps.Select(a => new SearchHit(
            SearchResultTypes.Application,
            a.Name,
            a.Slug,
            [],
            a.Description
        ));
    }

    private async Task<IEnumerable<SearchHit>> SecretsAsync(string pattern, CancellationToken ct)
    {
        var secrets = await db
            .Secrets.AsNoTracking()
            .Where(s => !s.Application.IsDeleted)
            .Where(s =>
                EF.Functions.Like(s.Name, pattern, Escape)
                || (s.Description != null && EF.Functions.Like(s.Description, pattern, Escape))
            )
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Description,
                AppSlug = s.Application.Slug,
            })
            .ToListAsync(ct);

        var ids = secrets.Select(s => s.Id).ToList();
        var environments = (
            await db
                .SecretVersions.AsNoTracking()
                .Where(v => ids.Contains(v.SecretId))
                .Select(v => new { v.SecretId, v.Environment.Slug })
                .Distinct()
                .ToListAsync(ct)
        ).ToLookup(v => v.SecretId, v => v.Slug);

        return secrets.Select(s => new SearchHit(
            SearchResultTypes.Secret,
            s.Name,
            s.AppSlug,
            Sorted(environments[s.Id]),
            s.Description
        ));
    }

    /// <summary>
    /// Finds matching keys first, then loads every environment holding each key, so a key whose
    /// description matched in one environment still lists all of its environments.
    /// </summary>
    private async Task<IEnumerable<SearchHit>> ConfigurationsAsync(
        string pattern,
        string term,
        CancellationToken ct
    )
    {
        var matched = await db
            .ConfigurationEntries.AsNoTracking()
            .Where(c => !c.Application.IsDeleted)
            .Where(c =>
                EF.Functions.Like(c.Key, pattern, Escape)
                || (c.Description != null && EF.Functions.Like(c.Description, pattern, Escape))
            )
            .Select(c => new { c.ApplicationId, c.Key })
            .Distinct()
            .ToListAsync(ct);
        if (matched.Count == 0)
            return [];

        var appIds = matched.Select(m => m.ApplicationId).Distinct().ToList();
        var keys = matched.Select(m => m.Key).Distinct().ToList();
        var wanted = matched.Select(m => (m.ApplicationId, m.Key)).ToHashSet();

        var rows = await db
            .ConfigurationEntries.AsNoTracking()
            .Where(c => appIds.Contains(c.ApplicationId) && keys.Contains(c.Key))
            .Select(c => new
            {
                c.ApplicationId,
                c.Key,
                c.Description,
                c.ValueType,
                AppSlug = c.Application.Slug,
                EnvSlug = c.Environment.Slug,
            })
            .ToListAsync(ct);

        return rows.Where(r => wanted.Contains((r.ApplicationId, r.Key)))
            .GroupBy(r => (r.AppSlug, r.Key))
            .Select(g => new SearchHit(
                SearchResultTypes.Configuration,
                g.Key.Key,
                g.Key.AppSlug,
                Sorted(g.Select(r => r.EnvSlug)),
                PickDescription(g.Select(r => r.Description), term),
                ConfigurationValueNormalizer.ToName(g.First().ValueType)
            ));
    }

    private async Task<IEnumerable<SearchHit>> GlobalSecretsAsync(
        string pattern,
        CancellationToken ct
    )
    {
        var secrets = await db
            .GlobalSecrets.AsNoTracking()
            .Where(s =>
                EF.Functions.Like(s.Name, pattern, Escape)
                || (s.Description != null && EF.Functions.Like(s.Description, pattern, Escape))
            )
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Description,
            })
            .ToListAsync(ct);

        var ids = secrets.Select(s => s.Id).ToList();
        var environments = (
            await db
                .GlobalSecretVersions.AsNoTracking()
                .Where(v => ids.Contains(v.GlobalSecretId))
                .Select(v => new { v.GlobalSecretId, v.EnvironmentSlug })
                .Distinct()
                .ToListAsync(ct)
        ).ToLookup(v => v.GlobalSecretId, v => v.EnvironmentSlug);

        return secrets.Select(s => new SearchHit(
            SearchResultTypes.GlobalSecret,
            s.Name,
            null,
            Sorted(environments[s.Id]),
            s.Description
        ));
    }

    /// <summary>Like <see cref="ConfigurationsAsync"/>: every environment of each matching key.</summary>
    private async Task<IEnumerable<SearchHit>> GlobalConfigurationsAsync(
        string pattern,
        string term,
        CancellationToken ct
    )
    {
        var keys = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Where(c =>
                EF.Functions.Like(c.Key, pattern, Escape)
                || (c.Description != null && EF.Functions.Like(c.Description, pattern, Escape))
            )
            .Select(c => c.Key)
            .Distinct()
            .ToListAsync(ct);
        if (keys.Count == 0)
            return [];

        var rows = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .Where(c => keys.Contains(c.Key))
            .Select(c => new
            {
                c.Key,
                c.Description,
                c.ValueType,
                c.EnvironmentSlug,
            })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.Key)
            .Select(g => new SearchHit(
                SearchResultTypes.GlobalConfiguration,
                g.Key,
                null,
                Sorted(g.Select(r => r.EnvironmentSlug)),
                PickDescription(g.Select(r => r.Description), term),
                ConfigurationValueNormalizer.ToName(g.First().ValueType)
            ));
    }

    /// <summary>
    /// The description containing the search term (so a description-only match still ranks),
    /// otherwise the first non-empty one.
    /// </summary>
    internal static string? PickDescription(IEnumerable<string?> descriptions, string term)
    {
        var present = descriptions.Where(d => !string.IsNullOrEmpty(d)).ToList();
        return present.FirstOrDefault(d => d!.Contains(term, StringComparison.OrdinalIgnoreCase))
            ?? present.FirstOrDefault();
    }

    /// <summary>Sorted, with <c>*</c> (all environments) first.</summary>
    private static List<string> Sorted(IEnumerable<string> environments) =>
        environments
            .Distinct()
            .OrderBy(e => e == GlobalConfigurationEntry.AllEnvironments ? 0 : 1)
            .ThenBy(e => e, StringComparer.Ordinal)
            .ToList();

    internal static string EscapeLike(string term) =>
        term.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_");
}
