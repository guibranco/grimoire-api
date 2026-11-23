using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence.Configurations;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>A consumer read buffered in memory until the next flush.</summary>
public sealed record PendingConsumerActivity(
    Guid ApplicationId,
    Guid EnvironmentId,
    DateTimeOffset LastSeenAt,
    string? LastUserAgent,
    string? LastIpAddress,
    long RequestCount
);

/// <summary>
/// In-memory buffer of consumer reads, one entry per application and environment. Recording is
/// lock-free and never touches the database; the flusher drains it. Drained entries stay
/// visible to <see cref="PendingLastSeen"/> as in-flight until <see cref="Complete"/> (saved) or
/// <see cref="Requeue"/> (failed), so a reader never misses a read that is being written.
/// </summary>
public sealed class ConsumerActivityTracker(TimeProvider time)
    : IConsumerActivityTracker,
        IFlushBuffer<PendingConsumerActivity>
{
    private readonly PendingBuffer<(Guid, Guid), PendingConsumerActivity> _buffer = new(
        e => (e.ApplicationId, e.EnvironmentId),
        Merge
    );

    public void Record(Guid applicationId, Guid environmentId, string? userAgent, string? ipAddress)
    {
        _buffer.Add(
            new PendingConsumerActivity(
                applicationId,
                environmentId,
                time.GetUtcNow(),
                Truncate(userAgent, ConsumerActivityConfiguration.UserAgentMaxLength),
                Truncate(ipAddress, ConsumerActivityConfiguration.IpAddressMaxLength),
                1
            )
        );
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingConsumerActivity> Drain() => _buffer.Drain();

    /// <inheritdoc />
    public void Complete(IEnumerable<PendingConsumerActivity> entries) => _buffer.Complete(entries);

    /// <inheritdoc />
    public void Requeue(IEnumerable<PendingConsumerActivity> entries) => _buffer.Requeue(entries);

    /// <summary>
    /// Last-seen times not yet stored (buffered or being written), so readers are fresh without
    /// waiting for a flush.
    /// </summary>
    public IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    > PendingLastSeen() =>
        _buffer
            .Snapshot((a, b) => b.LastSeenAt > a.LastSeenAt ? b : a)
            .ToDictionary(p => p.Key, p => p.Value.LastSeenAt);

    /// <summary>Combines two entries for the same key; the later read wins the client details.</summary>
    internal static PendingConsumerActivity Merge(
        PendingConsumerActivity a,
        PendingConsumerActivity b
    )
    {
        var latest = b.LastSeenAt >= a.LastSeenAt ? b : a;
        return latest with { RequestCount = a.RequestCount + b.RequestCount };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
