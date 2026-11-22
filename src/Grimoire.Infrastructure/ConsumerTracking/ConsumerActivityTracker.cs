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
/// lock-free and never touches the database; the flusher drains it. Drained entries stay
/// visible to <see cref="PendingLastSeen"/> as in-flight until <see cref="Complete"/> (saved) or
/// <see cref="Requeue"/> (failed), so a reader never misses a read that is being written.
/// </summary>
public sealed class ConsumerActivityTracker(TimeProvider time) : IConsumerActivityTracker
{
    private readonly ConcurrentDictionary<(Guid, Guid), PendingConsumerActivity> _pending = new();
    private readonly ConcurrentDictionary<(Guid, Guid), PendingConsumerActivity> _inFlight = new();

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

    /// <summary>
    /// Moves everything buffered so far to in-flight and returns it. Follow with
    /// <see cref="Complete"/> once saved, or <see cref="Requeue"/> if saving fails.
    /// </summary>
    public IReadOnlyList<PendingConsumerActivity> Drain()
    {
        var drained = new List<PendingConsumerActivity>(_pending.Count);
        foreach (var key in _pending.Keys)
        {
            if (!_pending.TryRemove(key, out var entry))
                continue;
            drained.Add(_inFlight.AddOrUpdate(key, entry, (_, existing) => Merge(existing, entry)));
        }
        return drained;
    }

    /// <summary>Forgets in-flight entries that were saved.</summary>
    public void Complete(IEnumerable<PendingConsumerActivity> entries)
    {
        foreach (var entry in entries)
            _inFlight.TryRemove(
                new KeyValuePair<(Guid, Guid), PendingConsumerActivity>(
                    (entry.ApplicationId, entry.EnvironmentId),
                    entry
                )
            );
    }

    /// <summary>Puts entries back after a failed flush, merged with anything recorded since.</summary>
    public void Requeue(IEnumerable<PendingConsumerActivity> entries)
    {
        foreach (var entry in entries)
        {
            var key = (entry.ApplicationId, entry.EnvironmentId);
            _pending.AddOrUpdate(key, entry, (_, existing) => Merge(entry, existing));
            _inFlight.TryRemove(
                new KeyValuePair<(Guid, Guid), PendingConsumerActivity>(key, entry)
            );
        }
    }

    /// <summary>
    /// Last-seen times not yet stored (buffered or being written), so readers are fresh without
    /// waiting for a flush.
    /// </summary>
    public IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    > PendingLastSeen()
    {
        var lastSeen = _inFlight.ToDictionary(p => p.Key, p => p.Value.LastSeenAt);
        foreach (var (key, entry) in _pending)
            if (!lastSeen.TryGetValue(key, out var seen) || entry.LastSeenAt > seen)
                lastSeen[key] = entry.LastSeenAt;
        return lastSeen;
    }

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
