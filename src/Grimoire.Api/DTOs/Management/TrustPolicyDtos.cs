namespace Grimoire.Api.DTOs.Management;

/// <param name="Name">Unique within the application.</param>
/// <param name="Repository"><c>owner/repo</c>.</param>
/// <param name="RefPattern">e.g. <c>refs/heads/main</c> or <c>refs/tags/v*</c>; omit for any ref.</param>
/// <param name="GitHubEnvironment">The job's GitHub environment; omit for any.</param>
/// <param name="Environments">Grimoire environment slugs; omit for all.</param>
/// <param name="Scopes"><c>secrets</c>, <c>configurations</c> and/or <c>feature-flags</c>.</param>
/// <param name="TokenLifetimeMinutes">1–60; defaults to 15.</param>
public record TrustPolicyRequest(
    string Name,
    string Repository,
    string? RefPattern,
    string? GitHubEnvironment,
    IReadOnlyList<string>? Environments,
    IReadOnlyList<string> Scopes,
    int? TokenLifetimeMinutes
);

public record TrustPolicyResponse(
    Guid Id,
    string Name,
    string Repository,
    string? RefPattern,
    string? GitHubEnvironment,
    IReadOnlyList<string>? Environments,
    IReadOnlyList<string> Scopes,
    int TokenLifetimeMinutes,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt
);
