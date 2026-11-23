using System.Security.Cryptography;
using Grimoire.Api.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.IntegrationTests;

/// <summary>Signs GitHub-like OIDC tokens with a key the test server trusts instead of GitHub's.</summary>
public static class TestOidc
{
    public const string Issuer = "https://token.actions.githubusercontent.com";
    public const string Audience = "grimoire";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public sealed class KeyProvider : IGitHubOidcKeyProvider
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<SecurityKey>>([Key]);
    }

    public static string Token(
        string repository = "guibranco/demo",
        string @ref = "refs/heads/main",
        string? environment = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        bool wrongKey = false
    )
    {
        var claims = new Dictionary<string, object>
        {
            ["repository"] = repository,
            ["ref"] = @ref,
            ["run_id"] = "4242",
            ["sub"] = $"repo:{repository}:ref:{@ref}",
        };
        if (environment is not null)
            claims["environment"] = environment;

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                IssuedAt = expiresAt.AddMinutes(-10),
                NotBefore = expiresAt.AddMinutes(-10),
                Expires = expiresAt,
                SigningCredentials = new SigningCredentials(
                    wrongKey ? OtherKey : Key,
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );
    }
}
