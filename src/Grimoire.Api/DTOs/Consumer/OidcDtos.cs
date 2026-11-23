namespace Grimoire.Api.DTOs.Consumer;

/// <param name="Application">The application slug to get a token for.</param>
/// <param name="Token">The GitHub Actions OIDC token (a JWT).</param>
public record GitHubOidcExchangeRequest(string Application, string Token);

/// <param name="Token">A <c>grm_tok_</c> access token; send it in <c>X-Api-Key</c>.</param>
public record GitHubOidcExchangeResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Environments,
    IReadOnlyList<string> Scopes,
    string Policy
);
