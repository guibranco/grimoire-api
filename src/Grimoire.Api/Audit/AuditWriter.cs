using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Audit;

namespace Grimoire.Api.Audit;

/// <summary>
/// Drains the audit queue into the database in batches and forwards each event to the log
/// pipeline (and so to LogStream) as a structured entry. Finishes queued events on shutdown.
/// </summary>
public sealed class AuditWriter(
    AuditLog auditLog,
    IServiceScopeFactory scopes,
    ILogger<AuditWriter> logger
) : BackgroundService
{
    private const int MaxBatch = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = auditLog.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
                await WriteAvailableAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: StopAsync writes what is left.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        auditLog.Complete();
        await WriteAvailableAsync(cancellationToken);
    }

    /// <summary>Writes everything queued right now, in batches.</summary>
    public async Task WriteAvailableAsync(CancellationToken ct)
    {
        var batch = new List<AuditEvent>(MaxBatch);
        while (auditLog.Reader.TryRead(out var auditEvent))
        {
            batch.Add(auditEvent);
            if (batch.Count == MaxBatch)
            {
                await SaveAsync(batch, ct);
                batch.Clear();
            }
        }
        await SaveAsync(batch, ct);

        if (auditLog.TakeDroppedCount() is > 0 and var dropped)
            logger.LogWarning("Audit queue was full; {Dropped} audit events were dropped", dropped);
    }

    private async Task SaveAsync(List<AuditEvent> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        if (logger.IsEnabled(LogLevel.Information))
            foreach (var e in batch)
                logger.LogInformation(
                    "Audit {AuditAction} {AuditOutcome} by {AuditActor} app={AuditApplication} env={AuditEnvironment} target={AuditTarget} status={AuditStatus}",
                    e.Action,
                    e.Outcome,
                    e.Actor,
                    e.ApplicationSlug,
                    e.Environment,
                    e.Target,
                    e.StatusCode
                );

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuditStore>().SaveAsync(batch, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Already forwarded to the log pipeline above, so the trail survives a failed write.
            logger.LogError(ex, "Failed to store {Count} audit events", batch.Count);
        }
    }
}
