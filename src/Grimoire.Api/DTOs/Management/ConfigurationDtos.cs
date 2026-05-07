namespace Grimoire.Api.DTOs.Management;

public record CreateConfigurationRequest(
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description = null
);

public record UpdateConfigurationRequest(string Value, string? Description = null);

public record ConfigurationEntryResponse(
    Guid Id,
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
