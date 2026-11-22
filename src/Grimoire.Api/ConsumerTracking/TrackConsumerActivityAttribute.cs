using Grimoire.Api.Middleware;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Grimoire.Api.ConsumerTracking;

/// <summary>
/// Records a successful (2xx) Consumer API read for the authenticated application and the
/// environment the action resolved. Failed, aborted and unauthorised calls are not recorded here.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TrackConsumerActivityAttribute : Attribute, IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context) { }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // A 2xx status alone isn't success: writing the result can still throw or be aborted.
        if (context.Exception is not null || context.Canceled)
            return;

        var http = context.HttpContext;
        if (http.Response.StatusCode is < 200 or >= 300)
            return;

        if (
            http.Items[ConsumerApiKeyMiddleware.AppContextKey] is not Application app
            || http.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] is not AppEnvironment env
        )
            return;

        var tracker = http.RequestServices.GetRequiredService<IConsumerActivityTracker>();
        tracker.Record(
            app.Id,
            env.Id,
            http.Request.Headers.UserAgent.ToString(),
            http.Connection.RemoteIpAddress?.ToString()
        );
    }
}
