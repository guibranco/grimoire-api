using Grimoire.Infrastructure.Audit;

namespace Grimoire.Api.Audit;

/// <summary>Deletes audit events older than <see cref="AuditOptions.RetentionDays"/>, at startup and daily.</summary>
public sealed class AuditRetentionService(
    AuditOptions options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<AuditRetentionService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RetentionDays <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);
        try
        {
            do
            {
                await PurgeAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <returns>Number of events deleted.</returns>
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        if (options.RetentionDays <= 0)
            return 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AuditStore>();
            var deleted = await store.DeleteOlderThanAsync(
                time.GetUtcNow().AddDays(-options.RetentionDays),
                ct
            );
            if (deleted > 0 && logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "Deleted {Count} audit events older than {Days} days",
                    deleted,
                    options.RetentionDays
                );
            return deleted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Audit retention cleanup failed");
            return 0;
        }
    }
}
