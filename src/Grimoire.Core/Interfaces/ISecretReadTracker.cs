namespace Grimoire.Core.Interfaces;

/// <summary>
/// Records successful Consumer API secret reads for the usage reports. Implementations must not
/// block on storage: a consumer read never waits for a database write.
/// </summary>
public interface ISecretReadTracker
{
    void Record(Guid applicationId, Guid environmentId, ValueSource source, Guid secretId);
}
