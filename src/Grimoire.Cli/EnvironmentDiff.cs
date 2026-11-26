namespace Grimoire.Cli;

internal enum DiffChange
{
    Added,
    Removed,
    Changed,
}

/// <summary>One difference between two environments. Secrets never carry values.</summary>
internal sealed record DiffItem(
    string Kind,
    string Key,
    DiffChange Change,
    string? From = null,
    string? To = null
);

/// <summary>
/// What differs between two environments: configuration keys by value, secrets only by whether
/// each has an active version (values are never compared or shown).
/// </summary>
internal static class EnvironmentDiff
{
    public static List<DiffItem> Compare(
        IReadOnlyDictionary<string, string> fromConfigurations,
        IReadOnlyDictionary<string, string> toConfigurations,
        IReadOnlySet<string> fromSecrets,
        IReadOnlySet<string> toSecrets
    )
    {
        var items = new List<DiffItem>();
        foreach (var key in Keys(fromConfigurations.Keys, toConfigurations.Keys))
        {
            var inFrom = fromConfigurations.TryGetValue(key, out var from);
            var inTo = toConfigurations.TryGetValue(key, out var to);
            if (!inFrom)
                items.Add(new DiffItem("config", key, DiffChange.Added, To: to));
            else if (!inTo)
                items.Add(new DiffItem("config", key, DiffChange.Removed, From: from));
            else if (!string.Equals(from, to, StringComparison.Ordinal))
                items.Add(new DiffItem("config", key, DiffChange.Changed, from, to));
        }
        foreach (var name in Keys(fromSecrets, toSecrets))
        {
            if (!fromSecrets.Contains(name))
                items.Add(new DiffItem("secret", name, DiffChange.Added));
            else if (!toSecrets.Contains(name))
                items.Add(new DiffItem("secret", name, DiffChange.Removed));
        }
        return items;
    }

    /// <summary>One line per difference: <c>+</c> only in the second environment, <c>-</c> only in the first, <c>~</c> changed.</summary>
    public static string Format(DiffItem item) =>
        item.Change switch
        {
            DiffChange.Added when item.Kind == "secret" => $"+ secret {item.Key}",
            DiffChange.Removed when item.Kind == "secret" => $"- secret {item.Key}",
            DiffChange.Added => $"+ config {item.Key}={Show(item.To)}",
            DiffChange.Removed => $"- config {item.Key}={Show(item.From)}",
            _ => $"~ config {item.Key}: {Show(item.From)} → {Show(item.To)}",
        };

    private static IEnumerable<string> Keys(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Union(b, StringComparer.Ordinal).Order(StringComparer.Ordinal);

    // Keep one difference per line.
    private static string Show(string? value) =>
        (value ?? "")
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
