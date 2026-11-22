using Grimoire.Core.Audit;
using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces;

/// <summary>
/// Accepts audit events without blocking the request: events are queued and written in the
/// background. Never pass secret or configuration values.
/// </summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent);
}

public interface IAuditQueryService
{
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
