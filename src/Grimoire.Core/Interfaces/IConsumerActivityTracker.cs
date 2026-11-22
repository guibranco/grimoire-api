namespace Grimoire.Core.Interfaces;

/// <summary>
/// Records successful Consumer API reads. Implementations must not block on storage: a consumer
/// read never waits for a database write.
/// </summary>
public interface IConsumerActivityTracker
{
    void Record(Guid applicationId, Guid environmentId, string? userAgent, string? ipAddress);
}
