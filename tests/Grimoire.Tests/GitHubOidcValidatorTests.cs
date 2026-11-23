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

    private static readonly DateTimeOffset Start = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The real provider over a metadata endpoint that serves <see cref="OldKey"/> until GitHub
    /// "rotates", then <see cref="NewKey"/>, and answers each fetch only after a delay.
    /// </summary>
    private sealed class Metadata
    {
        public bool Rotated { get; set; }
        public int Fetches { get; private set; }
        public FakeTimeProvider Time { get; } = new(Start);

        public GitHubOidcKeyProvider Provider() =>
            new(
                async ct =>
                {
                    await Task.Delay(50, ct);
                    Fetches++;
                    return [Rotated ? NewKey : OldKey];
                },
                Time
            );
    }

    private sealed class FailingKeys : IGitHubOidcKeyProvider
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct) =>
            throw new InvalidOperationException("GitHub is down");

        public Task<IReadOnlyCollection<SecurityKey>?> RefreshKeysAsync(CancellationToken ct) =>
            throw new InvalidOperationException("GitHub is down");
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
    public async Task AfterGitHubRotatesItsKeys_TheExchangeWaitsForTheNewKeys()
    {
        var metadata = new Metadata();
        var validator = Validator(metadata.Provider());
        Assert.NotNull(await validator.ValidateAsync(Token(OldKey), default));

        metadata.Rotated = true;
        metadata.Time.Advance(GitHubOidcKeyProvider.MinimumRefreshInterval);
        var claims = await validator.ValidateAsync(Token(NewKey), default);

        Assert.Equal("guibranco/demo", claims?.Repository);
        Assert.Equal(2, metadata.Fetches);
    }

    [Fact]
    public async Task UnknownKeys_CannotForceAFetchMoreThanEveryFewMinutes()
    {
        var metadata = new Metadata();
        var validator = Validator(metadata.Provider());
        Assert.NotNull(await validator.ValidateAsync(Token(OldKey), default));

        Assert.Null(await validator.ValidateAsync(Token(NewKey), default));
        Assert.Null(await validator.ValidateAsync(Token(NewKey), default));
        Assert.Equal(1, metadata.Fetches);
    }

    [Fact]
    public async Task OtherFailures_DoNotRefetchTheKeys()
    {
        var metadata = new Metadata();
        var validator = Validator(metadata.Provider());
        metadata.Time.Advance(GitHubOidcKeyProvider.MinimumRefreshInterval);
        var wrongIssuer = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = "https://elsewhere.example",
                Audience = Options.Audience,
                Claims = new Dictionary<string, object> { ["repository"] = "guibranco/demo" },
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(OldKey, SecurityAlgorithms.RsaSha256),
            }
        );
        Assert.Null(await validator.ValidateAsync(wrongIssuer, default));
        Assert.Equal(1, metadata.Fetches);
    }

    [Fact]
    public async Task TheKeysAreRefetchedOnceTheCacheExpires()
    {
        var metadata = new Metadata();
        var provider = metadata.Provider();
        await provider.GetKeysAsync(default);
        await provider.GetKeysAsync(default);
        metadata.Time.Advance(GitHubOidcKeyProvider.CacheLifetime);
        await provider.GetKeysAsync(default);
        Assert.Equal(2, metadata.Fetches);
    }

    [Fact]
    public async Task KeysThatCannotBeLoaded_RejectTheToken()
    {
        Assert.Null(await Validator(new FailingKeys()).ValidateAsync(Token(OldKey), default));
    }
}
