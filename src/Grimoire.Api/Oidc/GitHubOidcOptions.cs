namespace Grimoire.Api.Oidc;

/// <summary>Settings bound from the <c>Oidc:GitHub</c> configuration section.</summary>
public sealed class GitHubOidcOptions
{
    public const string SectionName = "Oidc:GitHub";

    /// <summary>Accepts GitHub Actions OIDC tokens at <c>/consumer/oidc/github</c>.</summary>
    public bool Enabled { get; set; } = true;

    public string Issuer { get; set; } = "https://token.actions.githubusercontent.com";

    /// <summary>
    /// The <c>aud</c> workflows must request, e.g. with
    /// <c>core.getIDToken('grimoire')</c>. Set it per instance (say, its URL) so a token minted for
    /// one Grimoire can't be replayed against another.
    /// </summary>
    public string Audience { get; set; } = "grimoire";
}
