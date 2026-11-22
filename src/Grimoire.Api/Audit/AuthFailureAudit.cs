using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;

namespace Grimoire.Api.Audit;

/// <summary>Records requests rejected by the API key middlewares (no application is known).</summary>
public static class AuthFailureAudit
{
    public static void Record(HttpContext context)
    {
        var auditLog = context.RequestServices.GetService<IAuditLog>();
        auditLog?.Record(
            new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = "anonymous",
                Action = AuditActions.AuthFailed,
                Target = $"{context.Request.Method} {context.Request.Path}",
                StatusCode = StatusCodes.Status401Unauthorized,
                Outcome = AuditOutcome.Forbidden,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
            }
        );
    }
}
