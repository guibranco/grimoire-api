namespace Grimoire.Api.DTOs.Management;

public record CreateSecretRequest(string Name, string? Description);

public record SetSecretValueRequest(
    string EnvironmentSlug,
    string Value,
    bool IsEnabled = true,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? NotBefore = null
);

public record SecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateSecretResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RequiredEnvironment> RequiredEnvironments
);

public record RequiredEnvironment(string Slug, string Name, bool ValueProvided);

public record SecretVersionMetadata(
    Guid Id,
    int Version,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NotBefore,
    DateTimeOffset CreatedAt
);
