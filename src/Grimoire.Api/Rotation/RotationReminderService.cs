namespace Grimoire.Api.Rotation;

/// <summary>Runs <see cref="RotationReminderNotifier"/> shortly after startup, then every <see cref="RotationReminderOptions.IntervalHours"/>.</summary>
public sealed class RotationReminderService(
    RotationReminderOptions options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<RotationReminderService> logger
) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        try
        {
            await Task.Delay(StartupDelay, time, stoppingToken);
            using var timer = new PeriodicTimer(
                TimeSpan.FromHours(Math.Max(1, options.IntervalHours)),
                time
            );
            do
            {
                await RunOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RotationReminderNotifier>().RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rotation reminder run failed");
        }
    }
}
