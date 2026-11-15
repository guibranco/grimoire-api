using Grimoire.Core.Interfaces;

namespace Grimoire.Api.DTOs.Management;

public record CreateGlobalConfigurationRequest(
    string Key,
    string Value,
    string? EnvironmentSlug = null,
    string? Description = null
);

public record UpdateGlobalConfigurationRequest(string Value, string? Description = null);

public record GlobalConfigurationEntryResponse(
    Guid Id,
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateGlobalSecretRequest(string Name, string? Description);

public record SetGlobalSecretValueRequest(
    string EnvironmentSlug,
    string Value,
    bool IsEnabled = true,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? NotBefore = null
);

public record GlobalSecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record GlobalEnvironmentResponse(string Slug, string Name, int ApplicationCount);

public record EffectiveConfigurationResponse(
    string Key,
    string Value,
    string? Description,
    string Source,
    string? GlobalEnvironmentSlug,
    bool OverridesGlobal
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
