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

/// <summary>One change to a configuration key. Configuration values aren't secret, so they're included.</summary>
/// <param name="Change"><c>baseline</c>, <c>create</c>, <c>update</c>, <c>delete</c> or <c>rollback</c>.</param>
/// <param name="Value">The value after the change; <c>null</c> for <c>delete</c>.</param>
/// <param name="RolledBackFrom">For <c>rollback</c>: the revision whose value was restored.</param>
public record ConfigurationRevisionResponse(
    int Revision,
    string Change,
    string? Value,
    string ValueType,
    string? Description,
    int? RolledBackFrom,
    string ChangedBy,
    DateTimeOffset ChangedAt
);

/// <param name="Source"><c>application</c> or <c>global</c>, as in the effective configuration.</param>
public record ConfigurationDiffSide(
    string Value,
    string ValueType,
    string Source,
    string? GlobalEnvironmentSlug
);

/// <param name="Status"><c>added</c> (only in <c>to</c>), <c>removed</c> (only in <c>from</c>), <c>changed</c> or <c>unchanged</c>.</param>
public record ConfigurationDiffItem(
    string Key,
    string Status,
    ConfigurationDiffSide? From,
    ConfigurationDiffSide? To
);

public record ConfigurationDiffResponse(
    string From,
    string To,
    IReadOnlyList<ConfigurationDiffItem> Items
);
