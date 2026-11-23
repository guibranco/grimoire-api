using Grimoire.Api.DTOs.Consumer;
using Grimoire.Core.Entities;

namespace Grimoire.Api.FeatureFlags;

/// <summary>
/// The flags an application environment receives, with the same precedence as configuration:
/// the application's own flag, else (when it inherits globals) the global flag with the state for
/// the environment's slug, else the global <c>*</c> state.
/// </summary>
public static class EffectiveFeatureFlags
{
    public static IReadOnlyList<ConsumerFeatureFlag> For(
        Application app,
        AppEnvironment env,
        IEnumerable<FeatureFlag> appFlags,
        IEnumerable<FeatureFlag> globalFlags
    )
    {
        var own = appFlags.ToList();
        var ownKeys = own.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var result = own.Select(f =>
            FeatureFlagMapper.ToConsumer(
                f,
                f.Environments.FirstOrDefault(s => s.EnvironmentId == env.Id)
            )
        );
        if (app.InheritGlobals)
        {
            var slug = new FlagScopeEnvironment(env.Slug, null);
            var global = FlagScope.Global([], []);
            result = result.Concat(
                globalFlags
                    .Where(f => !ownKeys.Contains(f.Key))
                    .Select(f => FeatureFlagMapper.ToConsumer(f, global.EffectiveStateOf(f, slug)))
            );
        }
        return result.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
    }
}
