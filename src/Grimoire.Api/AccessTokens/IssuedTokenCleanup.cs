using Grimoire.Core.Interfaces.Repositories;

namespace Grimoire.Api.AccessTokens;

/// <summary>
/// Deletes OIDC-issued tokens a day after they expire, at most once per <see cref="Interval"/>,
/// so the exchange doesn't run a bulk delete on every request. A failed cleanup never fails the
/// exchange: it's retried on a later one.
/// </summary>
public sealed class IssuedTokenCleanup(TimeProvider time, ILogger<IssuedTokenCleanup> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    private long _nextRunTicks;

    public async Task RunAsync(IAccessTokenRepository tokens, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var next = Interlocked.Read(ref _nextRunTicks);
        if (
            now.UtcTicks < next
            || Interlocked.CompareExchange(ref _nextRunTicks, (now + Interval).UtcTicks, next)
                != next
        )
            return;

        try
        {
            await tokens.DeleteExpiredIssuedAsync(now - Retention, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Deleting expired OIDC-issued access tokens failed.");
        }
    }
}
