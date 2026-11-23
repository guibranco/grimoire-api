using System.Collections.Concurrent;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>Something the consumer flusher drains, saves, then completes or requeues.</summary>
public interface IFlushBuffer<TEntry>
{
    /// <summary>
    /// Moves everything buffered so far to in-flight and returns it. Follow with
    /// <see cref="Complete"/> once saved, or <see cref="Requeue"/> if saving fails.
    /// </summary>
    IReadOnlyList<TEntry> Drain();

    /// <summary>Forgets in-flight entries that were saved.</summary>
    void Complete(IEnumerable<TEntry> entries);

    /// <summary>Puts entries back after a failed flush, merged with anything recorded since.</summary>
    void Requeue(IEnumerable<TEntry> entries);
}

/// <summary>
/// Lock-free in-memory buffer with one entry per key. Drained entries stay visible through
/// <see cref="Snapshot"/> as in-flight until completed or requeued, so a reader never misses
/// an entry that is being written.
/// </summary>
public sealed class PendingBuffer<TKey, TEntry>(
    Func<TEntry, TKey> keyOf,
    Func<TEntry, TEntry, TEntry> merge
) : IFlushBuffer<TEntry>
    where TKey : notnull
    where TEntry : class
{
    private readonly ConcurrentDictionary<TKey, TEntry> _pending = new();
    private readonly ConcurrentDictionary<TKey, TEntry> _inFlight = new();

    public void Add(TEntry entry) =>
        _pending.AddOrUpdate(keyOf(entry), entry, (_, existing) => merge(existing, entry));

    public IReadOnlyList<TEntry> Drain()
    {
        var drained = new List<TEntry>(_pending.Count);
        foreach (var key in _pending.Keys)
        {
            if (!_pending.TryRemove(key, out var entry))
                continue;
            drained.Add(_inFlight.AddOrUpdate(key, entry, (_, existing) => merge(existing, entry)));
        }
        return drained;
    }

    public void Complete(IEnumerable<TEntry> entries)
    {
        foreach (var entry in entries)
            _inFlight.TryRemove(new KeyValuePair<TKey, TEntry>(keyOf(entry), entry));
    }

    public void Requeue(IEnumerable<TEntry> entries)
    {
        foreach (var entry in entries)
        {
            var key = keyOf(entry);
            _pending.AddOrUpdate(key, entry, (_, existing) => merge(entry, existing));
            _inFlight.TryRemove(new KeyValuePair<TKey, TEntry>(key, entry));
        }
    }

    /// <summary>
    /// Everything not yet stored, buffered or being written, merged per key with the
    /// given picker (e.g. the later of two entries).
    /// </summary>
    public Dictionary<TKey, TEntry> Snapshot(Func<TEntry, TEntry, TEntry> pick)
    {
        var all = new Dictionary<TKey, TEntry>(_inFlight);
        foreach (var (key, entry) in _pending)
            all[key] = all.TryGetValue(key, out var seen) ? pick(seen, entry) : entry;
        return all;
    }
}
