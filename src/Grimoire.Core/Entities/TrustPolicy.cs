namespace Grimoire.Core.Entities;

/// <summary>
/// Lets GitHub Actions runs exchange their OIDC token for a short-lived access token
/// (guibranco/grimoire-api#75): runs from <see cref="Repository"/> whose ref and GitHub
/// environment match get a token for <see cref="EnvironmentSlugs"/> and <see cref="Scopes"/>.
/// </summary>
public class TrustPolicy
{
    public const int DefaultLifetimeMinutes = 15;
    public const int MaxLifetimeMinutes = 60;

    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>Unique per application, e.g. "deploy from main".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>owner/repo</c>, matched case-insensitively against the <c>repository</c> claim.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Matched against the <c>ref</c> claim, e.g. <c>refs/heads/main</c> or <c>refs/tags/v*</c>
    /// (<c>*</c> matches anything); <c>null</c> for any ref.
    /// </summary>
    public string? RefPattern { get; set; }

    /// <summary>Matched exactly against the <c>environment</c> claim; <c>null</c> for any (or none).</summary>
    public string? GitHubEnvironment { get; set; }

    /// <summary>Environments the issued token may read; <c>null</c> for all.</summary>
    public List<string>? EnvironmentSlugs { get; set; }

    public AccessTokenScopes Scopes { get; set; }

    public int TokenLifetimeMinutes { get; set; } = DefaultLifetimeMinutes;

    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Application Application { get; set; } = null!;
}
