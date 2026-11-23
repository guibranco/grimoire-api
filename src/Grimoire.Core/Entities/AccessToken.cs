namespace Grimoire.Core.Entities;

/// <summary>What a Consumer API access token may read.</summary>
[Flags]
public enum AccessTokenScopes
{
    None = 0,
    ReadSecrets = 1,
    ReadConfigurations = 2,
    ReadFeatureFlags = 4,
    All = ReadSecrets | ReadConfigurations | ReadFeatureFlags,
}

/// <summary>
/// A Consumer API credential for one application, narrower than its API key: limited to some
/// environments and scopes, optionally expiring, and revocable on its own. Only a PBKDF2 hash of
/// the token is kept; the plain token is shown once, when it is created.
/// </summary>
public class AccessToken
{
    /// <summary>Every token starts with this; the API key never does.</summary>
    public const string TokenPrefix = "grm_tok_";

    /// <summary>Characters after <see cref="TokenPrefix"/> kept to find (and show) a token.</summary>
    public const int LookupLength = 8;

    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>What the token is for, e.g. "github-actions"; unique per application.</summary>
    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The <see cref="LookupLength"/> characters after <see cref="TokenPrefix"/>.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Environments the token may read; <c>null</c> means all of them.</summary>
    public List<string>? EnvironmentSlugs { get; set; }

    public AccessTokenScopes Scopes { get; set; }

    /// <summary>Always UTC; <c>null</c> means it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Updated at most once a minute per token.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Application Application { get; set; } = null!;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expires && expires <= now;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public bool AllowsEnvironment(string environment) =>
        EnvironmentSlugs is null
        || EnvironmentSlugs.Contains(environment, StringComparer.OrdinalIgnoreCase);

    public bool AllowsScope(AccessTokenScopes scope) =>
        scope != AccessTokenScopes.None && (Scopes & scope) == scope;
}
