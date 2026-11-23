using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.Api.Oidc;

/// <summary>The keys GitHub signs its Actions OIDC tokens with.</summary>
public interface IGitHubOidcKeyProvider
{
    /// <summary>The cached keys, fetched when missing or older than the cache lifetime.</summary>
    Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct);

    /// <summary>
    /// Fetches the keys again now and returns them, as when GitHub rotates its keys; <c>null</c>
    /// when they were fetched too recently, so unknown key IDs can't force a fetch per request.
    /// </summary>
    Task<IReadOnlyCollection<SecurityKey>?> RefreshKeysAsync(CancellationToken ct);
}

/// <summary>
/// Reads the keys from the issuer's OpenID configuration (<c>/.well-known/openid-configuration</c>
/// and its <c>jwks_uri</c>) and caches them. A refresh waits for the fetch to finish, unlike
/// <see cref="ConfigurationManager{T}.RequestRefresh"/>, which only starts one in the background.
/// </summary>
public sealed class GitHubOidcKeyProvider : IGitHubOidcKeyProvider, IDisposable
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly Func<CancellationToken, Task<IReadOnlyCollection<SecurityKey>>> _fetch;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyCollection<SecurityKey>? _keys;
    private DateTimeOffset _fetchedAt;

    public GitHubOidcKeyProvider(GitHubOidcOptions options, TimeProvider time)
        : this(FetchFrom($"{options.Issuer.TrimEnd('/')}/.well-known/openid-configuration"), time)
    { }

    /// <summary>For tests: keys come from <paramref name="fetch"/>.</summary>
    public GitHubOidcKeyProvider(
        Func<CancellationToken, Task<IReadOnlyCollection<SecurityKey>>> fetch,
        TimeProvider time
    )
    {
        _fetch = fetch;
        _time = time;
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetKeysAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_keys is null || _time.GetUtcNow() - _fetchedAt >= CacheLifetime)
                await FetchAsync(ct);
            return _keys!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyCollection<SecurityKey>?> RefreshKeysAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_keys is not null && _time.GetUtcNow() - _fetchedAt < MinimumRefreshInterval)
                return null;
            await FetchAsync(ct);
            return _keys;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task FetchAsync(CancellationToken ct)
    {
        _keys = await _fetch(ct);
        _fetchedAt = _time.GetUtcNow();
    }

    private static Func<CancellationToken, Task<IReadOnlyCollection<SecurityKey>>> FetchFrom(
        string metadataAddress
    )
    {
        var documents = new HttpDocumentRetriever { RequireHttps = true };
        return async ct =>
            (
                await OpenIdConnectConfigurationRetriever.GetAsync(metadataAddress, documents, ct)
            ).SigningKeys.ToList();
    }
}
