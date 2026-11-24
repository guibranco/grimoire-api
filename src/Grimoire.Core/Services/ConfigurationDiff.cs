using Grimoire.Core.Interfaces;
using Grimoire.Core.Publishing;

namespace Grimoire.Core.Services;

public static class ConfigurationDiffStatus
{
    /// <summary>Only in the "to" environment.</summary>
    public const string Added = "added";

    /// <summary>Only in the "from" environment.</summary>
    public const string Removed = "removed";

    /// <summary>In both, with a different value or value type.</summary>
    public const string Changed = "changed";

    public const string Unchanged = "unchanged";
}

/// <param name="From">The entry in the "from" environment; <c>null</c> when <see cref="Status"/> is added.</param>
/// <param name="To">The entry in the "to" environment; <c>null</c> when <see cref="Status"/> is removed.</param>
public sealed record ConfigurationDiffEntry(
    string Key,
    string Status,
    EffectiveConfigurationEntry? From,
    EffectiveConfigurationEntry? To
);

/// <summary>Compares the effective configuration of two environments, key by key.</summary>
public static class ConfigurationDiff
{
    public static List<ConfigurationDiffEntry> Compare(
        IEnumerable<EffectiveConfigurationEntry> from,
        IEnumerable<EffectiveConfigurationEntry> to,
        bool includeUnchanged = false
    )
    {
        // Sentinels differ between environments by design: not a configuration difference.
        var left = from.Where(e => !ConfigurationSentinel.IsSentinel(e.Key))
            .ToDictionary(e => e.Key, StringComparer.Ordinal);
        var right = to.Where(e => !ConfigurationSentinel.IsSentinel(e.Key))
            .ToDictionary(e => e.Key, StringComparer.Ordinal);

        return left
            .Keys.Union(right.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key =>
            {
                left.TryGetValue(key, out var a);
                right.TryGetValue(key, out var b);
                return new ConfigurationDiffEntry(key, StatusOf(a, b), a, b);
            })
            .Where(d => includeUnchanged || d.Status != ConfigurationDiffStatus.Unchanged)
            .ToList();
    }

    private static string StatusOf(EffectiveConfigurationEntry? a, EffectiveConfigurationEntry? b)
    {
        if (a is null)
            return ConfigurationDiffStatus.Added;
        if (b is null)
            return ConfigurationDiffStatus.Removed;
        return a.Value == b.Value && a.ValueType == b.ValueType
            ? ConfigurationDiffStatus.Unchanged
            : ConfigurationDiffStatus.Changed;
    }
}
