using Grimoire.Api.Middleware;
using Grimoire.Core.Entities;

namespace Grimoire.Api.Audit;

/// <summary>
/// The audit actor for a Consumer API caller: <c>app:{slug}</c> for the API key, and
/// <c>app:{slug}/token:{prefix}</c> for an access token.
/// </summary>
public static class ConsumerActor
{
    public static string Of(Application app, AccessToken? token) =>
        token is null ? $"app:{app.Slug}" : $"app:{app.Slug}/token:{token.Prefix}";

    public static string? Of(HttpContext context) =>
        context.Items[ConsumerApiKeyMiddleware.AppContextKey] is Application app
            ? Of(app, context.Items[ConsumerApiKeyMiddleware.TokenContextKey] as AccessToken)
            : null;
}
