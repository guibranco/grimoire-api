namespace Grimoire.Api.DTOs.Management;

public record CreateEnvironmentRequest(string Name);

/// <param name="LastSeenAt">Last successful Consumer API read for this environment; <c>null</c> when never seen.</param>
public record EnvironmentResponse(
    Guid Id,
    string Name,
    string Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt = null
);
