namespace Grimoire.Api.DTOs.Management;

public record CreateEnvironmentRequest(string Name);

public record EnvironmentResponse(Guid Id, string Name, string Slug, DateTimeOffset CreatedAt);
