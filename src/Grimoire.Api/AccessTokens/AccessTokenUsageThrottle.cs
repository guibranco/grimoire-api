using System.Collections.Concurrent;

namespace Grimoire.Api.AccessTokens;

/// <summary>Lets each token's <c>LastUsedAt</c> be written at most once per <see cref="Interval"/>.</summary>
public sealed class AccessTokenUsageThrottle(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastWritten = new();

    /// <summary>True, with the time to record, when the token's last write is a minute old or more.</summary>
    public bool TryAcquire(Guid tokenId, out DateTimeOffset now)
    {
        now = time.GetUtcNow();
        while (true)
        {
            if (!_lastWritten.TryGetValue(tokenId, out var previous))
            {
                if (_lastWritten.TryAdd(tokenId, now))
                    return true;
                continue;
            }
            if (now - previous < Interval)
                return false;
            if (_lastWritten.TryUpdate(tokenId, now, previous))
                return true;
        }
    }
}
