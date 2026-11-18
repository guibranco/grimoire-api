namespace Grimoire.Api.DTOs.Management;

/// <param name="ValueType">
/// "string" (default), "multiline", "array" or "json". Array values accept a JSON array of
/// strings or one item per line; both are stored as a JSON array.
/// </param>
public record CreateConfigurationRequest(
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description = null,
    string? ValueType = null
);

/// <param name="ValueType">Leave out to keep the entry's current value type.</param>
public record UpdateConfigurationRequest(
    string Value,
    string? Description = null,
    string? ValueType = null
);

public record ConfigurationEntryResponse(
    Guid Id,
    string EnvironmentSlug,
    string Key,
    string Value,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ValueType
);
