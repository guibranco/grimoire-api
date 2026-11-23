using System.Security.Cryptography;
using Grimoire.Api.Oidc;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.Tests;

public class GitHubOidcValidatorTests
{
    private static readonly GitHubOidcOptions Options = new();
    private static readonly RsaSecurityKey OldKey = new(RSA.Create(2048)) { KeyId = "old" };
    private static readonly RsaSecurityKey NewKey = new(RSA.Create(2048)) { KeyId = "new" };

    /// <summary>Serves <c>OldKey</c> until a refresh is requested, then <c>NewKey</c>.</summary>
    private sealed class RotatingKeys : IGitHubOidcKeyProvider
    {
        public int Refreshes { get; private set; }

        public Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<SecurityKey>>([Refreshes == 0 ? OldKey : NewKey]);

        public void RequestRefresh() => Refreshes++;
    }

    private sealed class FailingKeys : IGitHubOidcKeyProvider
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct) =>
            throw new InvalidOperationException("GitHub is down");

        public void RequestRefresh() { }
    }

    private static string Token(SecurityKey key) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Options.Issuer,
                Audience = Options.Audience,
                Claims = new Dictionary<string, object>
                {
                    ["repository"] = "guibranco/demo",
                    ["ref"] = "refs/heads/main",
                },
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            }
        );

    private static GitHubOidcValidator Validator(IGitHubOidcKeyProvider keys) =>
        new(Options, keys, new FakeTimeProvider(DateTimeOffset.UtcNow));

    [Fact]
    public async Task ATokenSignedWithARotatedKey_RefreshesTheKeysOnce_AndIsAccepted()
    {
        var keys = new RotatingKeys();
        var claims = await Validator(keys).ValidateAsync(Token(NewKey), default);
        Assert.Equal("guibranco/demo", claims?.Repository);
        Assert.Equal(1, keys.Refreshes);
    }

    [Fact]
    public async Task OtherFailures_DoNotRefreshTheKeys()
    {
        var keys = new RotatingKeys();
        var expired = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = "https://elsewhere.example",
                Audience = Options.Audience,
                Claims = new Dictionary<string, object> { ["repository"] = "guibranco/demo" },
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(OldKey, SecurityAlgorithms.RsaSha256),
            }
        );
        Assert.Null(await Validator(keys).ValidateAsync(expired, default));
        Assert.Equal(0, keys.Refreshes);
    }

    [Fact]
    public async Task KeysThatCannotBeLoaded_RejectTheToken()
    {
        Assert.Null(await Validator(new FailingKeys()).ValidateAsync(Token(OldKey), default));
    }
}
