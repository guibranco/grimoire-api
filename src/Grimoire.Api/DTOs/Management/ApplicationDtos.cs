namespace Grimoire.Api.DTOs.Management;

public record CreateApplicationRequest(string Name, string? Description);
public record UpdateApplicationRequest(string Name, string? Description);

public record ApplicationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateApplicationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string PlainApiKey,
    DateTimeOffset CreatedAt);

public record RotateKeyResponse(string PlainApiKey);
