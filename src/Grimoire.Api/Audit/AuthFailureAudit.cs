using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;

namespace Grimoire.Api.Audit;

/// <summary>Records requests rejected by the API key middlewares.</summary>
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

    /// <summary>A valid access token used outside its environments or scopes.</summary>
    public static void RecordForbidden(HttpContext context, Application app, AccessToken token)
    {
        var auditLog = context.RequestServices.GetService<IAuditLog>();
        var environment = context.Request.Query["environment"].ToString();
        auditLog?.Record(
            new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = ConsumerActor.Of(app, token),
                Action = AuditActions.AccessTokenDenied,
                ApplicationSlug = app.Slug,
                Environment = environment.Length > 0 ? environment : null,
                TargetType = AuditActions.TargetType(AuditActions.AccessTokenDenied),
                Target = $"{context.Request.Method} {context.Request.Path}",
                StatusCode = StatusCodes.Status403Forbidden,
                Outcome = AuditOutcome.Forbidden,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
            }
        );
    }
}
