using Grimoire.Core.Entities;

namespace Grimoire.Core.Interfaces.Repositories;

public interface IAccessTokenRepository
{
    /// <summary>The application's tokens, newest first, revoked ones included.</summary>
    Task<List<AccessToken>> ListAsync(Guid applicationId, CancellationToken ct = default);

    Task<AccessToken?> GetAsync(Guid applicationId, Guid id, CancellationToken ct = default);

    /// <summary>Tokens whose lookup prefix matches; the caller verifies the hash.</summary>
    Task<List<AccessToken>> FindByPrefixAsync(string prefix, CancellationToken ct = default);

    Task<bool> NameExistsAsync(Guid applicationId, string name, CancellationToken ct = default);

    Task AddAsync(AccessToken token, CancellationToken ct = default);

    Task UpdateAsync(AccessToken token, CancellationToken ct = default);

    /// <summary>Sets <see cref="AccessToken.LastUsedAt"/> without loading the token.</summary>
    Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken ct = default);
}
