using System.Text.RegularExpressions;
using Grimoire.Core.Entities;

namespace Grimoire.Api.Oidc;

/// <summary>Whether a GitHub run's claims satisfy a trust policy.</summary>
public static class TrustPolicyMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The matching policy to use when several match: the most specific one (a GitHub
    /// environment, then a ref pattern, then fewer wildcards), then by name.
    /// </summary>
    public static TrustPolicy? BestMatch(
        IEnumerable<TrustPolicy> policies,
        GitHubOidcClaims claims
    ) =>
        policies
            .Where(p => Matches(p, claims))
            .OrderByDescending(p => p.GitHubEnvironment is not null)
            .ThenByDescending(p => p.RefPattern is not null)
            .ThenBy(p => p.RefPattern?.Count(c => c == '*') ?? 0)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    public static bool Matches(TrustPolicy policy, GitHubOidcClaims claims) =>
        string.Equals(policy.Repository, claims.Repository, StringComparison.OrdinalIgnoreCase)
        && (policy.RefPattern is null || RefMatches(policy.RefPattern, claims.Ref))
        && (
            policy.GitHubEnvironment is null
            || string.Equals(policy.GitHubEnvironment, claims.Environment, StringComparison.Ordinal)
        );

    /// <summary><c>*</c> matches any characters (slashes included); everything else is literal.</summary>
    public static bool RefMatches(string pattern, string value)
    {
        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        try
        {
            return Regex.IsMatch(value, regex, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
