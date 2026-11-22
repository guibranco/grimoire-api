namespace Grimoire.Api.DTOs.Management;

/// <param name="Action">Action code, e.g. <c>secret.read</c>, <c>configuration.update</c>.</param>
/// <param name="Description">Human-readable action, e.g. "Added secret version".</param>
/// <param name="Actor"><c>admin</c>, <c>app:&lt;slug&gt;</c> or <c>anonymous</c>.</param>
/// <param name="Application">Application slug; <c>null</c> for Global entries and rejected requests.</param>
/// <param name="Environment">Environment slug(s), comma-separated when one request touched several.</param>
/// <param name="TargetType"><c>application</c>, <c>environment</c>, <c>secret</c>, <c>configuration</c> or <c>backup</c>.</param>
/// <param name="Target">Secret name, configuration key or entry name. Never a value.</param>
/// <param name="Version">Secret version created or changed, when the request touched exactly one.</param>
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
    int? Version,
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

/// <summary>Query string of <c>GET /management/audit</c>. All filters optional.</summary>
public sealed class AuditListRequest
{
    /// <summary>Application slug.</summary>
    public string? Application { get; init; }

    /// <summary>Action code, or a prefix ending in <c>*</c> (e.g. <c>secret.*</c>).</summary>
    public string? Action { get; init; }

    /// <summary><c>admin</c>, <c>app:&lt;slug&gt;</c> or <c>anonymous</c>.</summary>
    public string? Actor { get; init; }

    /// <summary>Environment slug.</summary>
    public string? Environment { get; init; }

    /// <summary>Secret name or configuration key.</summary>
    public string? Target { get; init; }

    /// <summary><c>success</c>, <c>notfound</c>, <c>forbidden</c>, <c>invalid</c>, <c>conflict</c> or <c>error</c>.</summary>
    public string? Outcome { get; init; }

    /// <summary>Inclusive lower bound (ISO 8601).</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound (ISO 8601).</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Include consumer reads (<c>*.read</c>). Default <c>true</c>.</summary>
    public bool IncludeReads { get; init; } = true;

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page, 1–200 (default 50).</summary>
    public int PageSize { get; init; } = 50;
}
