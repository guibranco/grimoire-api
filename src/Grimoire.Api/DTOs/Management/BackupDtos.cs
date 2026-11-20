namespace Grimoire.Api.DTOs.Management;

public record BackupResponse(
    string Name,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Targets
);
