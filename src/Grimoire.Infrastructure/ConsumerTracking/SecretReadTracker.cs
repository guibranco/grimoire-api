using Grimoire.Core.Interfaces;

namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>A secret read buffered in memory until the next flush.</summary>
public sealed record PendingSecretRead(
    Guid ApplicationId,
    Guid EnvironmentId,
    ValueSource Source,
    Guid SecretId,
    DateTimeOffset LastReadAt,
    long ReadCount
);

/// <summary>
/// In-memory buffer of secret reads, one entry per reading environment and secret. Drained by
/// the consumer activity flusher together with last-seen tracking.
/// </summary>
public sealed class SecretReadTracker(TimeProvider time)
    : ISecretReadTracker,
        IFlushBuffer<PendingSecretRead>
{
    private readonly PendingBuffer<(Guid, ValueSource, Guid), PendingSecretRead> _buffer = new(
        e => (e.EnvironmentId, e.Source, e.SecretId),
        Merge
    );

    public void Record(Guid applicationId, Guid environmentId, ValueSource source, Guid secretId)
    {
        if (secretId == Guid.Empty)
            return;
        _buffer.Add(
            new PendingSecretRead(
                applicationId,
                environmentId,
                source,
                secretId,
                time.GetUtcNow(),
                1
            )
        );
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingSecretRead> Drain() => _buffer.Drain();

    /// <inheritdoc />
    public void Complete(IEnumerable<PendingSecretRead> entries) => _buffer.Complete(entries);

    /// <inheritdoc />
    public void Requeue(IEnumerable<PendingSecretRead> entries) => _buffer.Requeue(entries);

    /// <summary>Reads not yet stored (buffered or being written), so reports are fresh.</summary>
    public IReadOnlyCollection<PendingSecretRead> Pending() => _buffer.Snapshot(Merge).Values;

    internal static PendingSecretRead Merge(PendingSecretRead a, PendingSecretRead b) =>
        a with
        {
            LastReadAt = b.LastReadAt > a.LastReadAt ? b.LastReadAt : a.LastReadAt,
            ReadCount = a.ReadCount + b.ReadCount,
        };
}
