using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Grimoire.Api.Middleware;

public class AdminApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string Prefix = "/management";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(Prefix))
        {
            await next(context);
            return;
        }

        var adminKey = configuration["Management:AdminApiKey"];
        if (
            string.IsNullOrWhiteSpace(adminKey)
            || !context.Request.Headers.TryGetValue("Authorization", out var authHeader)
            || !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || !KeysMatch(authHeader.ToString()["Bearer ".Length..].Trim(), adminKey)
        )
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                    title = "Unauthorized",
                    status = 401,
                }
            );
            return;
        }

        await next(context);
    }

    // Compare SHA-256 digests in constant time so neither the key's contents
    // nor its length can be inferred from response timing.
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected))
        );
}
