namespace Grimoire.Api.DTOs.Management;

/// <param name="Action">Action code, e.g. <c>secret.read</c>, <c>configuration.update</c>.</param>
/// <param name="Description">Human-readable action, e.g. "Added secret version".</param>
/// <param name="Actor"><c>admin</c>, <c>app:&lt;slug&gt;</c> or <c>anonymous</c>.</param>
/// <param name="Application">Application slug; <c>null</c> for Global entries and rejected requests.</param>
/// <param name="Environment">Environment slug(s), comma-separated when one request touched several.</param>
/// <param name="TargetType"><c>application</c>, <c>environment</c>, <c>secret</c>, <c>configuration</c> or <c>backup</c>.</param>
/// <param name="Target">Secret name, configuration key or entry name. Never a value.</param>
/// <param name="Outcome"><c>success</c>, <c>notfound</c>, <c>forbidden</c>, <c>invalid</c>, <c>conflict</c> or <c>error</c>.</param>
public record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Description,
    string? Application,
    string? Environment,
    string? TargetType,
    string? Target,
    string Outcome,
    int StatusCode,
    string? IpAddress,
    string? UserAgent
);

public record AuditPageResponse(
    IReadOnlyList<AuditEventResponse> Items,
    int Page,
    int PageSize,
    int TotalCount
);

/// <summary>Dashboard "recent activity" entry; <c>action</c> is the human-readable description.</summary>
public record DashboardActivityResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Code,
    string? Application,
    string? Environment,
    string? Target,
    string? TargetType,
    string Outcome
);
