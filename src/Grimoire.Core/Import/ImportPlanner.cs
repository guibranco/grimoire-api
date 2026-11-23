using Grimoire.Core.Entities;

namespace Grimoire.Core.Import;

public enum ImportKind
{
    Configuration,
    Secret,
}

public enum ImportAction
{
    /// <summary>New key, or a secret with no value in this environment yet.</summary>
    Create,

    /// <summary>The key exists with another value and <c>onConflict</c> is <c>overwrite</c>.</summary>
    Update,

    /// <summary>The key already has this value.</summary>
    Unchanged,

    /// <summary>The key exists with another value and <c>onConflict</c> is <c>skip</c>.</summary>
    Skip,

    /// <summary>The key exists as the other kind (a secret imported as configuration, or the reverse); never applied.</summary>
    Conflict,

    /// <summary>Left out by the caller.</summary>
    Excluded,
}

/// <summary>What the target already holds for the imported keys, in the import's environment.</summary>
/// <param name="Configuration">Configuration entries by key: value and type.</param>
/// <param name="Secrets">Secrets by name: the value currently served here, or <c>null</c> when the secret has none here.</param>
public sealed record ImportSnapshot(
    IReadOnlyDictionary<string, (string Value, ConfigurationValueType Type)> Configuration,
    IReadOnlyDictionary<string, string?> Secrets
);

/// <summary>One key of an import plan.</summary>
/// <param name="Key">The key.</param>
/// <param name="Kind">Whether it is imported as a secret or configuration.</param>
/// <param name="Action">What applying the plan does with it.</param>
/// <param name="Value">The value (never sent back to callers for secrets).</param>
/// <param name="Type">The configuration value type.</param>
public sealed record ImportPlanItem(
    string Key,
    ImportKind Kind,
    ImportAction Action,
    string Value,
    ConfigurationValueType Type
);

/// <summary>How imported keys are classified and what happens to them.</summary>
public static class ImportPlanner
{
    /// <summary>Keys that look like credentials, matched case-insensitively.</summary>
    public static readonly IReadOnlyList<string> DefaultSecretKeys =
    [
        "*Password*",
        "*Secret*",
        "*Token*",
        "ConnectionStrings:*",
    ];

    /// <summary>
    /// The plan for <paramref name="values"/>: each key's kind (from <paramref name="kinds"/>,
    /// else <paramref name="secretKeys"/>) and action against <paramref name="snapshot"/>.
    /// </summary>
    public static List<ImportPlanItem> Plan(
        IEnumerable<ImportedValue> values,
        ImportSnapshot snapshot,
        IReadOnlyList<string> secretKeys,
        IReadOnlyDictionary<string, ImportKind> kinds,
        IReadOnlySet<string> excluded,
        bool overwrite
    ) =>
        values
            .Select(v =>
            {
                var kind = kinds.TryGetValue(v.Key, out var k)
                    ? k
                    : (Matches(v.Key, secretKeys) ? ImportKind.Secret : ImportKind.Configuration);
                var action = excluded.Contains(v.Key)
                    ? ImportAction.Excluded
                    : ActionFor(v, kind, snapshot, overwrite);
                return new ImportPlanItem(v.Key, kind, action, v.Value, v.Type);
            })
            .ToList();

    private static ImportAction ActionFor(
        ImportedValue v,
        ImportKind kind,
        ImportSnapshot snapshot,
        bool overwrite
    )
    {
        if (kind == ImportKind.Secret)
        {
            if (snapshot.Configuration.ContainsKey(v.Key))
                return ImportAction.Conflict;
            if (!snapshot.Secrets.TryGetValue(v.Key, out var current) || current is null)
                return ImportAction.Create;
            return Compare(current == v.Value, overwrite);
        }

        if (snapshot.Secrets.ContainsKey(v.Key))
            return ImportAction.Conflict;
        if (!snapshot.Configuration.TryGetValue(v.Key, out var entry))
            return ImportAction.Create;
        return Compare(entry.Value == v.Value && entry.Type == v.Type, overwrite);
    }

    private static ImportAction Compare(bool same, bool overwrite) =>
        same ? ImportAction.Unchanged
        : overwrite ? ImportAction.Update
        : ImportAction.Skip;

    /// <summary>Whether <paramref name="key"/> matches any glob (<c>*</c> = any run of characters), ignoring case.</summary>
    public static bool Matches(string key, IEnumerable<string> globs) =>
        globs.Any(g => GlobMatch(key.AsSpan(), g.AsSpan()));

    private static bool GlobMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> glob)
    {
        // Iterative wildcard match with single backtrack point: linear-ish, no regex.
        int t = 0,
            g = 0,
            star = -1,
            mark = 0;
        while (t < text.Length)
        {
            if (g < glob.Length && glob[g] == '*')
            {
                star = g++;
                mark = t;
            }
            else if (
                g < glob.Length
                && char.ToUpperInvariant(glob[g]) == char.ToUpperInvariant(text[t])
            )
            {
                g++;
                t++;
            }
            else if (star >= 0)
            {
                g = star + 1;
                t = ++mark;
            }
            else
                return false;
        }
        while (g < glob.Length && glob[g] == '*')
            g++;
        return g == glob.Length;
    }
}
