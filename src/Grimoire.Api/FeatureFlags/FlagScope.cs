using Grimoire.Core.Entities;

namespace Grimoire.Api.FeatureFlags;

/// <summary>
/// One environment a flag can have state in: an application environment (by id) or, for global
/// flags, an environment slug (<c>*</c> for every environment).
/// </summary>
public sealed record FlagScopeEnvironment(string Slug, Guid? Id)
{
    public bool Owns(FeatureFlagEnvironmentState state) =>
        Id is { } id
            ? state.EnvironmentId == id
            : state.EnvironmentId is null && state.EnvironmentSlug == Slug;

    public FeatureFlagEnvironmentState NewState(Guid flagId) =>
        new()
        {
            FeatureFlagId = flagId,
            EnvironmentId = Id,
            EnvironmentSlug = Id is null ? Slug : null,
        };
}

/// <summary>
/// Where flags live: one application (<see cref="ApplicationId"/>) with its environments, or the
/// global flags (<c>null</c>) with <c>*</c> and every known environment slug.
/// </summary>
public sealed record FlagScope(
    Guid? ApplicationId,
    string? ApplicationSlug,
    IReadOnlyList<FlagScopeEnvironment> Environments
)
{
    public const string AllEnvironments = GlobalConfigurationEntry.AllEnvironments;

    public bool IsGlobal => ApplicationId is null;

    /// <summary>
    /// The environment for <paramref name="slug"/>: one of the application's, or for globals any
    /// valid slug (also ones no application has yet). <c>null</c> when there is none.
    /// </summary>
    public FlagScopeEnvironment? Resolve(string slug, Func<string, bool> isValidGlobalSlug) =>
        Environments.FirstOrDefault(e => e.Slug == slug)
        ?? (IsGlobal && isValidGlobalSlug(slug) ? new FlagScopeEnvironment(slug, null) : null);

    public static FeatureFlagEnvironmentState? StateOf(
        FeatureFlag flag,
        FlagScopeEnvironment env
    ) => flag.Environments.FirstOrDefault(env.Owns);

    /// <summary>
    /// The state a consumer in <paramref name="env"/> gets: the exact one, and for globals the
    /// <c>*</c> state when the slug has none.
    /// </summary>
    public FeatureFlagEnvironmentState? EffectiveStateOf(
        FeatureFlag flag,
        FlagScopeEnvironment env
    ) =>
        StateOf(flag, env)
        ?? (
            IsGlobal
                ? flag.Environments.FirstOrDefault(s => s.EnvironmentSlug == AllEnvironments)
                : null
        );

    /// <summary>The global scope: <c>*</c>, then every known slug, then slugs only states use.</summary>
    public static FlagScope Global(IEnumerable<string> knownSlugs, IEnumerable<FeatureFlag> flags)
    {
        var slugs = knownSlugs
            .Concat(
                flags
                    .SelectMany(f => f.Environments)
                    .Select(s => s.EnvironmentSlug)
                    .OfType<string>()
            )
            .Where(s => s != AllEnvironments)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return new FlagScope(
            null,
            null,
            [
                new FlagScopeEnvironment(AllEnvironments, null),
                .. slugs.Select(s => new FlagScopeEnvironment(s, null)),
            ]
        );
    }

    public static FlagScope For(Application app, IEnumerable<AppEnvironment> environments) =>
        new(
            app.Id,
            app.Slug,
            environments.Select(e => new FlagScopeEnvironment(e.Slug, e.Id)).ToList()
        );
}
