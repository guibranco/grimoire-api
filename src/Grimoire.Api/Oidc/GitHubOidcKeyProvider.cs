using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.Api.Oidc;

/// <summary>The keys GitHub signs its Actions OIDC tokens with.</summary>
public interface IGitHubOidcKeyProvider
{
    Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct);

    /// <summary>
    /// Asks for the keys to be fetched again on the next <see cref="GetKeysAsync"/>, as when GitHub
    /// rotates them. Rate-limited, so unknown key IDs can't force a fetch on every request.
    /// </summary>
    void RequestRefresh();
}

/// <summary>
/// Reads the keys from the issuer's OpenID configuration (<c>/.well-known/openid-configuration</c>
/// and its <c>jwks_uri</c>), cached and refreshed by <see cref="ConfigurationManager{T}"/>.
/// </summary>
public sealed class GitHubOidcKeyProvider(GitHubOidcOptions options) : IGitHubOidcKeyProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration = new(
        $"{options.Issuer.TrimEnd('/')}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true }
    );

    public async Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct) =>
        (await _configuration.GetConfigurationAsync(ct)).SigningKeys.ToList();

    public void RequestRefresh() => _configuration.RequestRefresh();
}
