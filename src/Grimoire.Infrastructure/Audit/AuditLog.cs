using System.Threading.Channels;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence.Configurations;

namespace Grimoire.Infrastructure.Audit;

/// <summary>
/// Bounded in-memory queue of audit events. <see cref="Record"/> never waits: when the queue is
/// full the event is dropped and counted, so a burst of reads can't stall requests. A background
/// writer drains <see cref="Reader"/> into the database.
/// </summary>
public sealed class AuditLog : IAuditLog
{
    private readonly Channel<AuditEvent> _channel;
    private readonly bool _enabled;
    private long _dropped;

    public AuditLog(AuditOptions options)
    {
        _enabled = options.Enabled;
        _channel = Channel.CreateBounded<AuditEvent>(
            new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = false,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _dropped)
        );
    }

    public ChannelReader<AuditEvent> Reader => _channel.Reader;

    /// <summary>Events dropped because the queue was full, since the last call.</summary>
    public long TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    public void Record(AuditEvent auditEvent)
    {
        if (!_enabled)
            return;

        if (auditEvent.Id == Guid.Empty)
            auditEvent.Id = Guid.CreateVersion7();
        auditEvent.OccurredAt = auditEvent.OccurredAt.ToUniversalTime();
        auditEvent.Actor = Truncate(auditEvent.Actor, AuditEventConfiguration.ShortTextMaxLength)!;
        auditEvent.ApplicationSlug = Truncate(
            auditEvent.ApplicationSlug,
            AuditEventConfiguration.ShortTextMaxLength
        );
        auditEvent.Environment = Truncate(
            auditEvent.Environment,
            AuditEventConfiguration.EnvironmentMaxLength
        );
        auditEvent.Target = Truncate(auditEvent.Target, AuditEventConfiguration.TargetMaxLength);
        auditEvent.IpAddress = Truncate(
            auditEvent.IpAddress,
            AuditEventConfiguration.IpAddressMaxLength
        );
        auditEvent.UserAgent = Truncate(
            auditEvent.UserAgent,
            AuditEventConfiguration.UserAgentMaxLength
        );

        // With DropWrite, TryWrite reports success and the drop callback counts the overflow.
        _channel.Writer.TryWrite(auditEvent);
    }

    /// <summary>Stops accepting events; the writer finishes what is queued.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
