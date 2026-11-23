using Grimoire.Api.Oidc;
using Grimoire.Core.Entities;

namespace Grimoire.Tests;

public class TrustPolicyMatcherTests
{
    private static GitHubOidcClaims Run(
        string repo = "guibranco/demo",
        string @ref = "refs/heads/main",
        string? env = null
    ) => new(repo, @ref, env, "1");

    private static TrustPolicy Policy(
        string name = "p",
        string repo = "guibranco/demo",
        string? refPattern = null,
        string? env = null
    ) =>
        new()
        {
            Name = name,
            Repository = repo,
            RefPattern = refPattern,
            GitHubEnvironment = env,
        };

    [Theory]
    [InlineData("refs/heads/main", "refs/heads/main", true)]
    [InlineData("refs/heads/main", "refs/heads/main2", false)]
    [InlineData("refs/tags/v*", "refs/tags/v1.2.3", true)]
    [InlineData("refs/tags/v*", "refs/heads/v1", false)]
    [InlineData("refs/heads/*", "refs/heads/feature/x", true)]
    [InlineData("refs/heads/release-?.x", "refs/heads/release-1.x", false)]
    [InlineData("refs/heads/release-?.x", "refs/heads/release-?.x", true)]
    [InlineData("refs/heads/a.b", "refs/heads/aXb", false)]
    public void RefPatterns_UseStarAsTheOnlyWildcard(string pattern, string value, bool expected) =>
        Assert.Equal(expected, TrustPolicyMatcher.RefMatches(pattern, value));

    [Fact]
    public void Repository_IsCaseInsensitive_RefAndEnvironmentAreOptional()
    {
        Assert.True(TrustPolicyMatcher.Matches(Policy(repo: "GuiBranco/Demo"), Run()));
        Assert.False(TrustPolicyMatcher.Matches(Policy(repo: "guibranco/other"), Run()));
        Assert.False(TrustPolicyMatcher.Matches(Policy(env: "production"), Run()));
        Assert.True(TrustPolicyMatcher.Matches(Policy(env: "production"), Run(env: "production")));
        Assert.False(TrustPolicyMatcher.Matches(Policy(env: "production"), Run(env: "Production")));
        Assert.True(TrustPolicyMatcher.Matches(Policy(), Run(env: "anything")));
    }

    [Fact]
    public void BestMatch_PrefersTheMostSpecificPolicy()
    {
        var any = Policy("a-any");
        var tags = Policy("b-tags", refPattern: "refs/*");
        var main = Policy("c-main", refPattern: "refs/heads/main");
        var prod = Policy("d-prod", env: "production");
        var other = Policy("e-other", repo: "guibranco/other");
        TrustPolicy[] all = [any, tags, main, prod, other];

        Assert.Same(prod, TrustPolicyMatcher.BestMatch(all, Run(env: "production")));
        Assert.Same(main, TrustPolicyMatcher.BestMatch(all, Run()));
        Assert.Same(tags, TrustPolicyMatcher.BestMatch(all, Run(@ref: "refs/tags/v1")));
        Assert.Null(TrustPolicyMatcher.BestMatch([other], Run()));
        Assert.Same(any, TrustPolicyMatcher.BestMatch([Policy("z-any"), any], Run()));
    }
}
