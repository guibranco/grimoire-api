using Grimoire.Core.Entities;

namespace Grimoire.Api.AccessTokens;

/// <summary>The scope names used in the Management API, and which consumer route each covers.</summary>
public static class AccessTokenScopeNames
{
    public const string Secrets = "secrets";
    public const string Configurations = "configurations";
    public const string FeatureFlags = "feature-flags";

    private static readonly (string Name, AccessTokenScopes Scope)[] Map =
    [
        (Secrets, AccessTokenScopes.ReadSecrets),
        (Configurations, AccessTokenScopes.ReadConfigurations),
        (FeatureFlags, AccessTokenScopes.ReadFeatureFlags),
    ];

    public static IReadOnlyList<string> All { get; } = Map.Select(m => m.Name).ToArray();

    public static bool IsKnown(string name) => Map.Any(m => m.Name == name);

    public static AccessTokenScopes Parse(IEnumerable<string> names) =>
        names.Aggregate(AccessTokenScopes.None, (scopes, name) =>
            scopes | Map.FirstOrDefault(m => m.Name == name).Scope
        );

    public static IReadOnlyList<string> Names(AccessTokenScopes scopes) =>
        Map.Where(m => (scopes & m.Scope) == m.Scope).Select(m => m.Name).ToArray();

    /// <summary>
    /// The scope a Consumer API path needs (<c>/consumer/secrets/…</c> needs
    /// <see cref="AccessTokenScopes.ReadSecrets"/>), or <see cref="AccessTokenScopes.None"/>
    /// for a path no token may use.
    /// </summary>
    public static AccessTokenScopes RequiredFor(PathString path)
    {
        foreach (var (name, scope) in Map)
            if (path.StartsWithSegments($"/consumer/{name}"))
                return scope;
        return AccessTokenScopes.None;
    }
}
