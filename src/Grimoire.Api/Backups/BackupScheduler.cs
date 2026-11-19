using Cronos;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Backups;

namespace Grimoire.Api.Backups;

/// <summary>Runs <see cref="IBackupService.CreateBackupAsync"/> on the <c>Backup:Schedule</c> cron.</summary>
public sealed class BackupScheduler(
    BackupOptions options,
    IBackupService backups,
    TimeProvider time,
    ILogger<BackupScheduler> logger
) : BackgroundService
{
    /// <summary>Task.Delay caps at ~49.7 days; a yearly cron can be further away, so wait in steps.</summary>
    internal static readonly TimeSpan MaxStep = TimeSpan.FromDays(1);

    private async Task WaitUntilAsync(DateTimeOffset due, CancellationToken ct)
    {
        for (
            var remaining = due - time.GetUtcNow();
            remaining > TimeSpan.Zero;
            remaining = due - time.GetUtcNow()
        )
            await Task.Delay(remaining < MaxStep ? remaining : MaxStep, time, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!backups.IsConfigured || string.IsNullOrWhiteSpace(options.Schedule))
        {
            logger.LogWarning("Scheduled backups are disabled");
            return;
        }

        var cron = CronExpression.Parse(options.Schedule);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        logger.LogInformation(
            "Scheduled backups enabled: '{Schedule}' ({TimeZone}), keeping {Retention}",
            options.Schedule,
            zone.Id,
            options.RetentionCount
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var next = cron.GetNextOccurrence(now, zone);
            if (next is null)
                return;

            await WaitUntilAsync(next.Value, stoppingToken);

            try
            {
                await backups.CreateBackupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Already logged and reported to the healthcheck by the service; keep the schedule alive.
                logger.LogDebug(ex, "Scheduled backup failed; next attempt on the next occurrence");
            }
        }
    }
}
