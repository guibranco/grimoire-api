using System.Collections.Concurrent;
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
/// lock-free and never touches the database; <see cref="ConsumerActivityFlusher"/> drains it.
/// </summary>
public sealed class ConsumerActivityTracker(TimeProvider time) : IConsumerActivityTracker
{
    private readonly ConcurrentDictionary<(Guid, Guid), PendingConsumerActivity> _pending = new();

    public void Record(Guid applicationId, Guid environmentId, string? userAgent, string? ipAddress)
    {
        var entry = new PendingConsumerActivity(
            applicationId,
            environmentId,
            time.GetUtcNow(),
            Truncate(userAgent, ConsumerActivityConfiguration.UserAgentMaxLength),
            Truncate(ipAddress, ConsumerActivityConfiguration.IpAddressMaxLength),
            1
        );
        _pending.AddOrUpdate((applicationId, environmentId), entry, (_, existing) =>
            Merge(existing, entry)
        );
    }

    /// <summary>Removes and returns everything buffered so far.</summary>
    public IReadOnlyList<PendingConsumerActivity> Drain()
    {
        var drained = new List<PendingConsumerActivity>(_pending.Count);
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var entry))
                drained.Add(entry);
        return drained;
    }

    /// <summary>Puts entries back after a failed flush, merged with anything recorded since.</summary>
    public void Requeue(IEnumerable<PendingConsumerActivity> entries)
    {
        foreach (var entry in entries)
            _pending.AddOrUpdate((entry.ApplicationId, entry.EnvironmentId), entry, (_, existing) =>
                Merge(entry, existing)
            );
    }

    /// <summary>Last-seen times not yet flushed, so readers are fresh without waiting for a flush.</summary>
    public IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    > PendingLastSeen() => _pending.ToDictionary(p => p.Key, p => p.Value.LastSeenAt);

    /// <summary>Combines two entries for the same key; the later read wins the client details.</summary>
    internal static PendingConsumerActivity Merge(
        PendingConsumerActivity a,
        PendingConsumerActivity b
    )
    {
        var latest = b.LastSeenAt >= a.LastSeenAt ? b : a;
        return latest with { RequestCount = a.RequestCount + b.RequestCount };
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
