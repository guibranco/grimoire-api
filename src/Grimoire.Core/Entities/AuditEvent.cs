using Grimoire.Core.Audit;

namespace Grimoire.Core.Entities;

/// <summary>
/// One audited request: who did what to which application, environment and entry, and how it
/// ended. Never holds secret or configuration values.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }

    /// <summary>Always UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary><c>admin</c>, <c>app:&lt;slug&gt;</c> or <c>anonymous</c>.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>One of <see cref="AuditActions"/>, e.g. <c>secret.read</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Application slug, kept as text so events stay readable after the application is deleted.</summary>
    public string? ApplicationSlug { get; set; }

    /// <summary>Environment slug(s), comma-separated when one request touched several.</summary>
    public string? Environment { get; set; }

    /// <summary><c>application</c>, <c>environment</c>, <c>secret</c>, <c>configuration</c> or <c>backup</c>.</summary>
    public string? TargetType { get; set; }

    /// <summary>Secret name, configuration key or other entry name.</summary>
    public string? Target { get; set; }

    /// <summary>Secret version created or changed, when the request touched exactly one.</summary>
    public int? Version { get; set; }

    public AuditOutcome Outcome { get; set; }

    /// <summary>HTTP status code of the response.</summary>
    public int StatusCode { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
