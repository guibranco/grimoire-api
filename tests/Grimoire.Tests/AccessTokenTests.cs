using Grimoire.Api.AccessTokens;
using Grimoire.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Tests;

public class AccessTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Throttle_AllowsOneWritePerTokenPerMinute()
    {
        var time = new FakeTimeProvider(Now);
        var throttle = new AccessTokenUsageThrottle(time);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.True(throttle.TryAcquire(a, out var first));
        Assert.Equal(Now, first);
        Assert.False(throttle.TryAcquire(a, out _));
        Assert.True(throttle.TryAcquire(b, out _));

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(throttle.TryAcquire(a, out _));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(throttle.TryAcquire(a, out var second));
        Assert.Equal(Now.AddMinutes(1), second);
    }

    [Fact]
    public void Status_ReflectsRevocationAndExpiry()
    {
        var token = new AccessToken { ExpiresAt = Now.AddDays(1) };
        Assert.True(token.IsActive(Now));
        Assert.False(token.IsActive(Now.AddDays(1)));
        Assert.True(token.IsExpired(Now.AddDays(2)));

        token.ExpiresAt = null;
        Assert.True(token.IsActive(Now.AddYears(10)));
        token.RevokedAt = Now;
        Assert.False(token.IsActive(Now));
    }

    [Fact]
    public void Environments_NullMeansAll_ListIsCaseInsensitive()
    {
        var token = new AccessToken();
        Assert.True(token.AllowsEnvironment("production"));
        token.EnvironmentSlugs = ["staging"];
        Assert.True(token.AllowsEnvironment("Staging"));
        Assert.False(token.AllowsEnvironment("production"));
    }

    [Fact]
    public void Scopes_MustCoverTheRequestedOne()
    {
        var token = new AccessToken
        {
            Scopes = AccessTokenScopes.ReadSecrets | AccessTokenScopes.ReadFeatureFlags,
        };
        Assert.True(token.AllowsScope(AccessTokenScopes.ReadSecrets));
        Assert.False(token.AllowsScope(AccessTokenScopes.ReadConfigurations));
        Assert.False(token.AllowsScope(AccessTokenScopes.None));
    }

    [Fact]
    public void ScopeNames_RoundTrip()
    {
        var scopes = AccessTokenScopeNames.Parse(["secrets", "feature-flags"]);
        Assert.Equal(AccessTokenScopes.ReadSecrets | AccessTokenScopes.ReadFeatureFlags, scopes);
        Assert.Equal(["secrets", "feature-flags"], AccessTokenScopeNames.Names(scopes));
        Assert.Equal(AccessTokenScopeNames.All, AccessTokenScopeNames.Names(AccessTokenScopes.All));
        Assert.False(AccessTokenScopeNames.IsKnown("write"));
    }

    [Theory]
    [InlineData("/consumer/secrets/DB_PASSWORD", AccessTokenScopes.ReadSecrets)]
    [InlineData("/consumer/configurations", AccessTokenScopes.ReadConfigurations)]
    [InlineData("/consumer/configurations/Logging", AccessTokenScopes.ReadConfigurations)]
    [InlineData("/consumer/feature-flags/beta", AccessTokenScopes.ReadFeatureFlags)]
    [InlineData("/consumer/secretsx", AccessTokenScopes.None)]
    [InlineData("/consumer/other", AccessTokenScopes.None)]
    public void RequiredScope_FollowsTheConsumerRoute(string path, AccessTokenScopes expected) =>
        Assert.Equal(expected, AccessTokenScopeNames.RequiredFor(new PathString(path)));

    [Theory]
    [InlineData(null, AccessTokenScopes.ReadSecrets | AccessTokenScopes.ReadConfigurations)]
    [InlineData("configurations", AccessTokenScopes.ReadConfigurations)]
    [InlineData(
        " Secrets , configurations ",
        AccessTokenScopes.ReadSecrets | AccessTokenScopes.ReadConfigurations
    )]
    [InlineData("flags", AccessTokenScopes.ReadSecrets | AccessTokenScopes.ReadConfigurations)]
    public void RequiredScope_ForAnExport_FollowsItsInclude(
        string? include,
        AccessTokenScopes expected
    ) =>
        Assert.Equal(
            expected,
            AccessTokenScopeNames.RequiredFor(new PathString("/consumer/export"), include)
        );

    [Theory]
    [InlineData("secrets,flags")]
    [InlineData(",")]
    public void ExportInclude_RejectsUnknownOrEmptyLists(string include) =>
        Assert.False(AccessTokenScopeNames.TryParseExportInclude(include, out _));
}
