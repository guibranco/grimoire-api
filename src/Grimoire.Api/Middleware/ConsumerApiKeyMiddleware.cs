using Grimoire.Api.AccessTokens;
using Grimoire.Api.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Grimoire.Api.Middleware;

/// <summary>
/// Authenticates Consumer API requests by their <c>X-Api-Key</c> header: an access token
/// (<c>grm_tok_…</c>, limited to some environments and scopes) or the application's API key
/// (every environment and scope). Unknown, revoked or expired credentials get <c>401</c>; a token
/// used outside its environments or scopes gets <c>403</c>.
/// </summary>
public class ConsumerApiKeyMiddleware(RequestDelegate next)
{
    private const string Prefix = "/consumer";
    private const string OidcPrefix = "/consumer/oidc";
    public const string AppContextKey = "GrimoireApplication";

    /// <summary>The access token used, when the request was not made with the API key.</summary>
    public const string TokenContextKey = "GrimoireAccessToken";

    /// <summary>Set by consumer actions once the requested environment is resolved.</summary>
    public const string EnvironmentContextKey = "GrimoireEnvironment";

    public async Task InvokeAsync(
        HttpContext context,
        IApplicationRepository appRepo,
        IAccessTokenRepository tokenRepo,
        IPasswordHasher<Application> appHasher,
        IPasswordHasher<AccessToken> tokenHasher,
        AccessTokenUsageThrottle usage,
        TimeProvider time
    )
    {
        // The OIDC exchange is its own credential (guibranco/grimoire-api#75).
        if (
            !context.Request.Path.StartsWithSegments(Prefix)
            || context.Request.Path.StartsWithSegments(OidcPrefix)
        )
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            await WriteUnauthorized(context);
            return;
        }

        var rawKey = apiKeyHeader.ToString();
        if (rawKey.StartsWith(AccessToken.TokenPrefix, StringComparison.Ordinal))
        {
            await AuthenticateTokenAsync(
                context,
                rawKey,
                appRepo,
                tokenRepo,
                tokenHasher,
                usage,
                time
            );
            return;
        }

        // The API key: verify against every application (PBKDF2 hashes can't be looked up).
        var apps = await appRepo.GetAllAsync(context.RequestAborted);
        var matched = apps.FirstOrDefault(app =>
            appHasher.VerifyHashedPassword(app, app.ApiKeyHash, rawKey)
            != PasswordVerificationResult.Failed
        );

        if (matched is null)
        {
            await WriteUnauthorized(context);
            return;
        }

        context.Items[AppContextKey] = matched;
        await next(context);
    }

    private async Task AuthenticateTokenAsync(
        HttpContext context,
        string rawToken,
        IApplicationRepository appRepo,
        IAccessTokenRepository tokenRepo,
        IPasswordHasher<AccessToken> hasher,
        AccessTokenUsageThrottle usage,
        TimeProvider time
    )
    {
        var ct = context.RequestAborted;
        var lookup =
            rawToken.Length >= AccessToken.TokenPrefix.Length + AccessToken.LookupLength
                ? rawToken.Substring(AccessToken.TokenPrefix.Length, AccessToken.LookupLength)
                : null;
        var candidates = lookup is null ? [] : await tokenRepo.FindByPrefixAsync(lookup, ct);
        var token = candidates.FirstOrDefault(t =>
            hasher.VerifyHashedPassword(t, t.TokenHash, rawToken)
            != PasswordVerificationResult.Failed
        );

        var now = time.GetUtcNow();
        var app = token is null ? null : await appRepo.GetByIdAsync(token.ApplicationId, ct);
        if (token is null || app is null || !token.IsActive(now))
        {
            await WriteUnauthorized(context);
            return;
        }

        context.Items[AppContextKey] = app;
        context.Items[TokenContextKey] = token;

        var environment = context.Request.Query["environment"].ToString();
        var scope = AccessTokenScopeNames.RequiredFor(context.Request.Path);
        if (
            !token.AllowsScope(scope)
            || (environment.Length > 0 && !token.AllowsEnvironment(environment))
        )
        {
            AuthFailureAudit.RecordForbidden(context, app, token);
            await WriteForbidden(context);
            return;
        }

        if (usage.TryAcquire(token.Id, out var usedAt))
            await tokenRepo.TouchAsync(token.Id, usedAt, ct);

        await next(context);
    }

    private static Task WriteUnauthorized(HttpContext context)
    {
        AuthFailureAudit.Record(context);
        return WriteProblem(
            context,
            401,
            "Unauthorized",
            "https://tools.ietf.org/html/rfc7235#section-3.1"
        );
    }

    private static Task WriteForbidden(HttpContext context) =>
        WriteProblem(
            context,
            403,
            "This access token can't read that environment or kind of data.",
            "https://tools.ietf.org/html/rfc7231#section-6.5.3"
        );

    private static Task WriteProblem(HttpContext context, int status, string title, string type)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(
            new
            {
                type,
                title,
                status,
            },
            context.RequestAborted
        );
    }
}
