using Grimoire.Infrastructure.ConsumerTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Api.ConsumerTracking;

/// <summary>
/// Writes buffered consumer reads and secret reads to the database every
/// <see cref="ConsumerActivityOptions.FlushIntervalSeconds"/>, and once more on shutdown.
/// </summary>
public sealed class ConsumerActivityFlusher(
    ConsumerActivityTracker tracker,
    SecretReadTracker secretReads,
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

    /// <summary>
    /// Drains both buffers (last-seen and secret reads) into the database; on failure the
    /// entries are requeued.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        await FlushBufferAsync<PendingConsumerActivity, ConsumerActivityStore>(
            tracker,
            (store, entries, token) => store.SaveAsync(entries, token),
            "consumer activity",
            ct
        );
        await FlushBufferAsync<PendingSecretRead, SecretReadStore>(
            secretReads,
            (store, entries, token) => store.SaveAsync(entries, token),
            "secret read",
            ct
        );
    }

    private async Task FlushBufferAsync<TEntry, TStore>(
        IFlushBuffer<TEntry> buffer,
        Func<TStore, IReadOnlyList<TEntry>, CancellationToken, Task> save,
        string label,
        CancellationToken ct
    )
        where TStore : notnull
    {
        var entries = buffer.Drain();
        if (entries.Count == 0)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await save(scope.ServiceProvider.GetRequiredService<TStore>(), entries, ct);
            buffer.Complete(entries);
        }
        catch (OperationCanceledException)
        {
            // Cancelled (e.g. on shutdown) before the write finished: keep the entries.
            buffer.Requeue(entries);
            throw;
        }
        catch (Exception ex)
        {
            buffer.Requeue(entries);
            logger.LogWarning(
                ex,
                "Failed to flush {Count} {Kind} entries; they will be retried",
                entries.Count,
                label
            );
        }
    }
}
