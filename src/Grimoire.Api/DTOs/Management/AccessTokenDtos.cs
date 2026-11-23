namespace Grimoire.Api.DTOs.Management;

/// <param name="Name">What the token is for; unique within the application.</param>
/// <param name="Environments">Environment slugs it may read; omit or null for all.</param>
/// <param name="Scopes"><c>secrets</c>, <c>configurations</c> and/or <c>feature-flags</c>.</param>
/// <param name="ExpiresAt">When it stops working; omit or null for never.</param>
public record CreateAccessTokenRequest(
    string Name,
    IReadOnlyList<string>? Environments,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt
);

/// <param name="Status"><c>active</c>, <c>expired</c> or <c>revoked</c>.</param>
public record AccessTokenResponse(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string>? Environments,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    string Status
);

/// <param name="PlainToken">Shown only in this response; Grimoire keeps only its hash.</param>
public record CreateAccessTokenResponse(AccessTokenResponse Token, string PlainToken);
