namespace Grimoire.Core.Entities;

/// <summary>
/// Rollup of successful Consumer API reads of one secret by one application environment:
/// when it was last read and roughly how often. Exactly one of <see cref="SecretId"/> (an
/// application secret) and <see cref="GlobalSecretId"/> (a global secret) is set. Holds no values.
/// </summary>
public class SecretRead
{
    public Guid Id { get; set; }

    /// <summary>The application that read the secret.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>The reading application's environment.</summary>
    public Guid EnvironmentId { get; set; }

    public Guid? SecretId { get; set; }
    public Guid? GlobalSecretId { get; set; }

    /// <summary>Always UTC.</summary>
    public DateTimeOffset LastReadAt { get; set; }

    /// <summary>Approximate: incremented by the number of buffered reads on each flush.</summary>
    public long ReadCount { get; set; }

    public Application Application { get; set; } = null!;
    public AppEnvironment Environment { get; set; } = null!;
}
