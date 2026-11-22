namespace Grimoire.Core.Entities;

/// <summary>
/// Last successful Consumer API read per application and environment. Holds no keys, values or
/// request bodies: only when, from what client and (approximately) how often.
/// </summary>
public class ConsumerActivity
{
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>e.g. <c>Grimoire.Consumer/1.4.0</c>, <c>grimoire-php</c>, <c>grimoire-action</c>.</summary>
    public string? LastUserAgent { get; set; }
    public string? LastIpAddress { get; set; }

    /// <summary>Approximate: incremented by the number of buffered reads on each flush.</summary>
    public long RequestCount { get; set; }

    public Application Application { get; set; } = null!;
    public AppEnvironment Environment { get; set; } = null!;
}
