using System.Security.Cryptography;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Grimoire.Api.AccessTokens;

/// <summary>Creates access tokens (hashing the plain value) and checks requested environments.</summary>
public sealed class AccessTokenFactory(
    IPasswordHasher<AccessToken> hasher,
    IEnvironmentRepository environments,
    TimeProvider time
)
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int SecretLength = 40;

    /// <summary>A new token and its plain value, which is not kept anywhere.</summary>
    public (AccessToken Token, string Plain) Create(
        Guid applicationId,
        string name,
        List<string>? environmentSlugs,
        AccessTokenScopes scopes,
        DateTimeOffset? expiresAt,
        Guid? trustPolicyId = null
    )
    {
        var secret = RandomNumberGenerator.GetString(Base62, SecretLength);
        var plain = AccessToken.TokenPrefix + secret;
        var token = new AccessToken
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Name = name,
            Prefix = secret[..AccessToken.LookupLength],
            EnvironmentSlugs = environmentSlugs,
            Scopes = scopes,
            ExpiresAt = expiresAt?.ToUniversalTime(),
            CreatedAt = time.GetUtcNow(),
            TrustPolicyId = trustPolicyId,
        };
        token.TokenHash = hasher.HashPassword(token, plain);
        return (token, plain);
    }

    /// <summary>
    /// The requested slugs, lower-cased, de-duplicated and sorted (<c>null</c> stays <c>null</c>:
    /// all environments), or the slugs the application doesn't have.
    /// </summary>
    public async Task<(List<string>? Slugs, List<string> Unknown)> ResolveEnvironmentsAsync(
        Guid applicationId,
        IReadOnlyList<string>? requested,
        CancellationToken ct
    )
    {
        if (requested is null)
            return (null, []);
        var known = (await environments.GetByApplicationAsync(applicationId, ct))
            .Select(e => e.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = requested.Where(e => !known.Contains(e)).ToList();
        var slugs = requested
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return (unknown.Count > 0 ? null : slugs, unknown);
    }
}
