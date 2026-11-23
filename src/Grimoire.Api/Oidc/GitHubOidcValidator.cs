using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.Api.Oidc;

/// <summary>The claims of a GitHub Actions OIDC token that trust policies look at.</summary>
/// <param name="Repository"><c>owner/repo</c>.</param>
/// <param name="Ref">e.g. <c>refs/heads/main</c>; empty when absent.</param>
/// <param name="Environment">The job's GitHub environment, if any.</param>
/// <param name="RunId">The workflow run, for naming the issued token.</param>
public sealed record GitHubOidcClaims(
    string Repository,
    string Ref,
    string? Environment,
    string? RunId
);

/// <summary>Validates GitHub Actions OIDC tokens: signature, issuer, audience and lifetime.</summary>
public sealed class GitHubOidcValidator(
    GitHubOidcOptions options,
    IGitHubOidcKeyProvider keys,
    TimeProvider time
)
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>The token's claims, or <c>null</c> when it isn't a valid token for this instance.</summary>
    public async Task<GitHubOidcClaims?> ValidateAsync(string jwt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jwt) || !Handler.CanReadToken(jwt))
            return null;

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = await keys.GetKeysAsync(ct),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            LifetimeValidator = (notBefore, expires, _, p) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now + p.ClockSkew)
                    && expires is not null
                    && expires > now - p.ClockSkew;
            },
        };

        var result = await Handler.ValidateTokenAsync(jwt, parameters);
        if (!result.IsValid)
            return null;

        string? Claim(string name) =>
            result.Claims.TryGetValue(name, out var value) ? value?.ToString() : null;

        var repository = Claim("repository");
        return string.IsNullOrEmpty(repository)
            ? null
            : new GitHubOidcClaims(
                repository,
                Claim("ref") ?? "",
                Claim("environment"),
                Claim("run_id")
            );
    }
}
