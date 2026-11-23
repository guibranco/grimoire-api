namespace Grimoire.Api.DTOs.Management;

/// <param name="RotationIntervalDays">Rotate at least every this many days; <c>null</c> for no schedule.</param>
public record CreateSecretRequest(
    string Name,
    string? Description,
    int? RotationIntervalDays = null
);

/// <summary>Changes a secret's metadata (application or Global). Omitted fields keep their value.</summary>
/// <param name="ClearDescription">Remove the description (overrides <paramref name="Description"/>).</param>
/// <param name="ClearRotationInterval">Remove the rotation schedule (overrides <paramref name="RotationIntervalDays"/>).</param>
public record UpdateSecretRequest(
    string? Description = null,
    int? RotationIntervalDays = null,
    bool ClearDescription = false,
    bool ClearRotationInterval = false
);

/// <param name="PreviousVersionGraceDays">
/// When set, every other enabled version in this environment with no expiry (or a later one)
/// expires this many days from now, in the same transaction. <c>null</c> leaves them untouched.
/// </param>
public record SetSecretValueRequest(
    string EnvironmentSlug,
    string Value,
    bool IsEnabled = true,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? NotBefore = null,
    int? PreviousVersionGraceDays = null
);

/// <summary>Changes a version's window. Omitted fields keep their value; the value itself never changes.</summary>
/// <param name="ClearNotBefore">Remove the start date (overrides <paramref name="NotBefore"/>).</param>
/// <param name="ClearExpiresAt">Remove the expiry (overrides <paramref name="ExpiresAt"/>).</param>
public record UpdateSecretVersionRequest(
    bool? IsEnabled = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? ExpiresAt = null,
    bool ClearNotBefore = false,
    bool ClearExpiresAt = false
);

public record SecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? RotationIntervalDays = null
);

public record CreateSecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RequiredEnvironment> RequiredEnvironments,
    int? RotationIntervalDays = null
);

public record RequiredEnvironment(string Slug, string Name, bool ValueProvided);

/// <param name="IsCurrent">This is the version consumers get now.</param>
/// <param name="Status"><c>current</c>, <c>valid</c>, <c>scheduled</c>, <c>expired</c> or <c>disabled</c>.</param>
public record SecretVersionMetadata(
    Guid Id,
    int Version,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NotBefore,
    DateTimeOffset CreatedAt,
    bool IsCurrent = false,
    string Status = ""
);
