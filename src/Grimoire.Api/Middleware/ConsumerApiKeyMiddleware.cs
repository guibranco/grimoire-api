using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Grimoire.Api.Middleware;

public class ConsumerApiKeyMiddleware(RequestDelegate next)
{
    private const string Prefix = "/api/consumer";
    public const string AppContextKey = "GrimoireApplication";

    public async Task InvokeAsync(
        HttpContext context,
        IApplicationRepository appRepo,
        IPasswordHasher<Application> hasher
    )
    {
        if (!context.Request.Path.StartsWithSegments(Prefix))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            await WriteForbidden(context);
            return;
        }

        var rawKey = apiKeyHeader.ToString();
        // Find all apps and verify hash (can't query by hash directly since it uses the Argon2/PBKDF2 hasher)
        var apps = await appRepo.GetAllAsync();
        Application? matched = null;
        foreach (var app in apps)
        {
            var result = hasher.VerifyHashedPassword(app, app.ApiKeyHash, rawKey);
            if (result != PasswordVerificationResult.Failed)
            {
                matched = app;
                break;
            }
        }

        if (matched is null)
        {
            await WriteForbidden(context);
            return;
        }

        context.Items[AppContextKey] = matched;
        await next(context);
    }

    private static Task WriteForbidden(HttpContext context)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
            }
        );
    }
}
