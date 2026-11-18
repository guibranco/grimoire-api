using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;

namespace Grimoire.Core.Services;

/// <summary>Pure precedence rules for merging application and global configuration.</summary>
/// <remarks>
/// An application value replaces a global value as a whole, whatever its
/// <see cref="ConfigurationValueType"/>: arrays and JSON objects are never merged.
/// </remarks>
public static class ConfigurationPrecedence
{
    /// <summary>
    /// Keys are matched case-insensitively, like .NET configuration, so an application key
    /// overrides a global key that differs only by case (otherwise the consumer configuration
    /// provider would see two keys that collide).
    /// </summary>
    public static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<EffectiveConfigurationEntry> Merge(
        IEnumerable<ConfigurationEntry> applicationEntries,
        IEnumerable<GlobalConfigurationEntry> globalEntries,
        string environmentSlug,
        bool inheritGlobals
    )
    {
        var globals = inheritGlobals
            ? ResolveGlobals(globalEntries, environmentSlug)
            : new Dictionary<string, GlobalConfigurationEntry>(KeyComparer);

        var result = new Dictionary<string, EffectiveConfigurationEntry>(KeyComparer);

        foreach (var entry in applicationEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (result.ContainsKey(entry.Key))
                continue;
            globals.TryGetValue(entry.Key, out var shadowed);
            result[entry.Key] = new EffectiveConfigurationEntry(
                entry.Key,
                entry.Value,
                entry.Description,
                ValueSource.Application,
                shadowed?.EnvironmentSlug,
                shadowed is not null,
                entry.ValueType
            );
        }

        foreach (var (key, global) in globals)
        {
            if (result.ContainsKey(key))
                continue;
            result[key] = new EffectiveConfigurationEntry(
                global.Key,
                global.Value,
                global.Description,
                ValueSource.Global,
                global.EnvironmentSlug,
                false,
                global.ValueType
            );
        }

        return result.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Picks, per key, the global entry for <paramref name="environmentSlug"/> over the "*" one.
    /// Entries for other environments are ignored.
    /// </summary>
    public static Dictionary<string, GlobalConfigurationEntry> ResolveGlobals(
        IEnumerable<GlobalConfigurationEntry> globalEntries,
        string environmentSlug
    )
    {
        var resolved = new Dictionary<string, GlobalConfigurationEntry>(KeyComparer);
        var ordered = globalEntries
            .Where(g =>
                g.EnvironmentSlug == environmentSlug
                || g.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments
            )
            // Environment-specific entries first so they win over "*".
            .OrderBy(g => g.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in ordered)
            resolved.TryAdd(g.Key, g);

        return resolved;
    }
}
