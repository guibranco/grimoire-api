using Grimoire.Core.Entities;

namespace Grimoire.Core.Audit;

/// <summary>Filters for the audit log. All optional; text filters match exactly.</summary>
/// <param name="Action">Exact code, or a prefix ending in <c>*</c> (e.g. <c>secret.*</c>).</param>
/// <param name="IncludeReads">When <c>false</c>, <c>*.read</c> events are left out.</param>
public sealed record AuditQuery(
    string? Application = null,
    string? Action = null,
    string? Actor = null,
    string? Environment = null,
    string? Target = null,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeReads = true,
    int Page = 1,
    int PageSize = 50
);

public sealed record AuditPage(
    IReadOnlyList<AuditEvent> Items,
    int Page,
    int PageSize,
    int TotalCount
);
