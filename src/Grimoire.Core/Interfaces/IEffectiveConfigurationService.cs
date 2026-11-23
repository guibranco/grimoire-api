using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces;

/// <summary>Where an effective value comes from.</summary>
public enum ValueSource
{
    Application,
    Global,
}

/// <summary>
/// A configuration value as an application sees it after globals are merged in.
/// </summary>
/// <param name="GlobalEnvironmentSlug">
/// For <see cref="ValueSource.Global"/>, the slug of the global entry that supplied the value
/// ("*" or an environment slug). For an application value that overrides a global, the slug of
/// the global entry it shadows. Otherwise <c>null</c>.
/// </param>
public sealed record EffectiveConfigurationEntry(
    string Key,
    string Value,
    string? Description,
    ValueSource Source,
    string? GlobalEnvironmentSlug,
    bool OverridesGlobal,
    ConfigurationValueType ValueType = ConfigurationValueType.String
);

/// <summary>Secret metadata as an application sees it after globals are merged in.</summary>
public sealed record EffectiveSecret(
    string Name,
    string? Description,
    ValueSource Source,
    bool OverridesGlobal
);

public enum SecretResolutionStatus
{
    Resolved,
    SecretNotFound,
    NoActiveVersion,
}

/// <summary>
/// The active secret version picked for a consumer request. The value stays encrypted.
/// <paramref name="SecretId"/> is the application or global secret it came from (see <paramref name="Source"/>).
/// </summary>
public sealed record ResolvedSecretVersion(
    string Name,
    string EncryptedValue,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NotBefore,
    int Version,
    DateTimeOffset CreatedAt,
    ValueSource Source,
    Guid SecretId = default
);

public sealed record SecretResolution(SecretResolutionStatus Status, ResolvedSecretVersion? Value)
{
    public static readonly SecretResolution NotFound = new(
        SecretResolutionStatus.SecretNotFound,
        null
    );

    public static readonly SecretResolution NoActiveVersion = new(
        SecretResolutionStatus.NoActiveVersion,
        null
    );
}

/// <summary>
/// Merges application-scoped and global configuration/secrets.
/// Precedence (highest wins): application value, global value for the environment slug,
/// global value for "*". Globals only apply when <see cref="Application.InheritGlobals"/> is true.
/// </summary>
public interface IEffectiveConfigurationService
{
    Task<IReadOnlyList<EffectiveConfigurationEntry>> GetConfigurationAsync(
        Application application,
        AppEnvironment environment,
        CancellationToken ct = default
    );

    Task<EffectiveConfigurationEntry?> GetConfigurationValueAsync(
        Application application,
        AppEnvironment environment,
        string key,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<EffectiveSecret>> GetSecretsAsync(
        Application application,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves the secret a consumer receives. Falls back to a global secret only when the
    /// application has no secret with that name; an application secret without an active
    /// version is never replaced by a global one.
    /// </summary>
    Task<SecretResolution> ResolveSecretAsync(
        Application application,
        AppEnvironment environment,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default
    );
}
