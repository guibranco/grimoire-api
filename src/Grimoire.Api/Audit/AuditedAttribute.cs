using System.Collections;
using Grimoire.Api.Middleware;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Grimoire.Api.Audit;

/// <summary>
/// Records an audit event for the action once it has run: who (admin or the calling
/// application), what (<paramref name="action"/>), where (application, environment), which entry
/// and how it ended. Names come from route values, the <c>environment</c> query parameter and the
/// <c>Name</c> / <c>Key</c> / <c>EnvironmentSlug</c> properties of request bodies — never values.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditedAttribute(string action) : Attribute, IAsyncActionFilter
{
    private static readonly string[] TargetRouteKeys = ["name", "key"];
    private static readonly string[] EnvironmentRouteKeys = ["envSlug", "environmentSlug"];

    public string Action { get; } = action;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        var executed = await next();

        var http = context.HttpContext;
        var statusCode = StatusCodeOf(executed);
        var app = http.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        var bodies = context
            .ActionArguments.Values.Where(v => v is not null and not CancellationToken)
            .ToList();

        var auditEvent = new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Actor = app is not null ? $"app:{app.Slug}" : "admin",
            Action = Action,
            ApplicationSlug = Route(context, "slug") ?? app?.Slug ?? CreatedSlug(executed),
            Environment = EnvironmentOf(context, bodies),
            TargetType = AuditActions.TargetType(Action),
            Target = TargetOf(context, bodies),
            StatusCode = statusCode,
            Outcome = AuditActions.OutcomeFor(statusCode),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString(),
        };

        http.RequestServices.GetRequiredService<IAuditLog>().Record(auditEvent);
    }

    private static int StatusCodeOf(ActionExecutedContext executed)
    {
        if (executed.Exception is not null && !executed.ExceptionHandled)
            return StatusCodes.Status500InternalServerError;
        return executed.Result switch
        {
            IStatusCodeActionResult { StatusCode: { } code } => code,
            ObjectResult { Value: ProblemDetails { Status: { } status } } => status,
            _ => StatusCodes.Status200OK,
        };
    }

    private static string? Route(ActionExecutingContext context, string key) =>
        context.RouteData.Values.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>Slug of an application just created (from the Created-at-action route).</summary>
    private static string? CreatedSlug(ActionExecutedContext executed) =>
        executed.Result is CreatedAtActionResult { RouteValues: { } values }
        && values.TryGetValue("slug", out var slug)
            ? slug?.ToString()
            : null;

    private static string? TargetOf(ActionExecutingContext context, List<object?> bodies)
    {
        foreach (var key in TargetRouteKeys)
            if (Route(context, key) is { } fromRoute)
                return fromRoute;
        return bodies
            .SelectMany(Items)
            .Select(b => Property(b, "Key") ?? Property(b, "Name"))
            .FirstOrDefault(v => v is not null);
    }

    private static string? EnvironmentOf(ActionExecutingContext context, List<object?> bodies)
    {
        foreach (var key in EnvironmentRouteKeys)
            if (Route(context, key) is { } fromRoute)
                return fromRoute;

        var fromQuery = context.HttpContext.Request.Query["environment"].ToString();
        if (!string.IsNullOrWhiteSpace(fromQuery))
            return fromQuery;

        // e.g. setting a secret's value in several environments at once.
        var fromBodies = bodies
            .SelectMany(Items)
            .Select(b => Property(b, "EnvironmentSlug"))
            .OfType<string>()
            .Distinct()
            .ToList();
        return fromBodies.Count > 0 ? string.Join(',', fromBodies) : null;
    }

    private static IEnumerable<object> Items(object? body) =>
        body switch
        {
            null or string => [],
            IEnumerable list => list.Cast<object?>().OfType<object>(),
            _ => [body],
        };

    /// <summary>Reads only the named string property (a name, key or slug — never a value).</summary>
    private static string? Property(object body, string name) =>
        body.GetType().GetProperty(name)?.GetValue(body) as string is { Length: > 0 } text
            ? text
            : null;
}
