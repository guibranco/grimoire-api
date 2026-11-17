namespace Grimoire.Api.DTOs.Management;

/// <param name="InheritGlobals">Defaults to <c>true</c> when omitted.</param>
public record CreateApplicationRequest(
    string Name,
    string? Description,
    bool? InheritGlobals = null
);

/// <param name="InheritGlobals">Left unchanged when omitted.</param>
public record UpdateApplicationRequest(
    string Name,
    string? Description,
    bool? InheritGlobals = null
);

public record ApplicationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    bool InheritGlobals,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <param name="PlainApiKey">The application API key in plain text. Returned only once; it is stored hashed.</param>
public record CreateApplicationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string PlainApiKey,
    DateTimeOffset CreatedAt,
    bool InheritGlobals
);

/// <param name="PlainApiKey">The new application API key in plain text. Returned only once; it is stored hashed.</param>
public record RotateKeyResponse(string PlainApiKey);
