using Grimoire.Core.Interfaces;

namespace Grimoire.Api.DTOs.Management;

/// <param name="ValueType">"string" (default), "multiline", "array" or "json".</param>
public record CreateGlobalConfigurationRequest(
    string Key,
    string Value,
    string? EnvironmentSlug = null,
    string? Description = null,
    string? ValueType = null
);

/// <param name="ValueType">Leave out to keep the entry's current value type.</param>
public record UpdateGlobalConfigurationRequest(
    string Value,
    string? Description = null,
    string? ValueType = null
);

public record GlobalConfigurationEntryResponse(
    Guid Id,
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ValueType
);

/// <param name="RotationIntervalDays">See <see cref="CreateSecretRequest.RotationIntervalDays"/>.</param>
public record CreateGlobalSecretRequest(
    string Name,
    string? Description,
    int? RotationIntervalDays = null
);

/// <param name="PreviousVersionGraceDays">See <see cref="SetSecretValueRequest.PreviousVersionGraceDays"/>.</param>
public record SetGlobalSecretValueRequest(
    string EnvironmentSlug,
    string Value,
    bool IsEnabled = true,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? NotBefore = null,
    int? PreviousVersionGraceDays = null
);

public record GlobalSecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? RotationIntervalDays = null
);

public record GlobalEnvironmentResponse(string Slug, string Name, int ApplicationCount);

public record EffectiveConfigurationResponse(
    string Key,
    string Value,
    string? Description,
    string Source,
    string? GlobalEnvironmentSlug,
    bool OverridesGlobal,
    string ValueType
);

public record EffectiveSecretResponse(
    string Name,
    string? Description,
    string Source,
    bool OverridesGlobal
);

public static class ValueSourceNames
{
    public const string Application = "application";
    public const string Global = "global";

    public static string ToName(ValueSource source) =>
        source == ValueSource.Global ? Global : Application;
}
