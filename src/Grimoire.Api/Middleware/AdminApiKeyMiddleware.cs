using Microsoft.AspNetCore.Http;

namespace Grimoire.Api.Middleware;

public class AdminApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string Prefix = "/api/management";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(Prefix))
        {
            await next(context);
            return;
        }

        var adminKey = configuration["Management:AdminApiKey"];
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader)
            || !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || authHeader.ToString()["Bearer ".Length..].Trim() != adminKey)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401
            });
            return;
        }

        await next(context);
    }
}
