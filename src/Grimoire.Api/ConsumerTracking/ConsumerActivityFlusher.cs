using Grimoire.Infrastructure.ConsumerTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Api.ConsumerTracking;

/// <summary>
/// Writes buffered consumer reads to the database every
/// <see cref="ConsumerActivityOptions.FlushIntervalSeconds"/>, and once more on shutdown.
/// </summary>
public sealed class ConsumerActivityFlusher(
    ConsumerActivityTracker tracker,
    IServiceScopeFactory scopes,
    ConsumerActivityOptions options,
    TimeProvider time,
    ILogger<ConsumerActivityFlusher> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.FlushIntervalSeconds));
        using var timer = new PeriodicTimer(interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: StopAsync performs the final flush.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Drains the buffer into the database; on failure the entries are requeued.</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var entries = tracker.Drain();
        if (entries.Count == 0)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ConsumerActivityStore>();
            await store.SaveAsync(entries, ct);
            tracker.Complete(entries);
        }
        catch (OperationCanceledException)
        {
            // Cancelled (e.g. on shutdown) before the write finished: keep the entries.
            tracker.Requeue(entries);
            throw;
        }
        catch (Exception ex)
        {
            tracker.Requeue(entries);
            logger.LogWarning(
                ex,
                "Failed to flush {Count} consumer activity entries; they will be retried",
                entries.Count
            );
        }
    }
}
