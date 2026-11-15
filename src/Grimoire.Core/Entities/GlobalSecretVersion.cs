namespace Grimoire.Core.Entities;

/// <summary>
/// A versioned value of a <see cref="GlobalSecret"/>, targeting an environment by slug
/// (or <see cref="GlobalConfigurationEntry.AllEnvironments"/> for every environment).
/// </summary>
public class GlobalSecretVersion
{
    public Guid Id { get; set; }
    public Guid GlobalSecretId { get; set; }
    public string EnvironmentSlug { get; set; } = GlobalConfigurationEntry.AllEnvironments;
    public string EncryptedValue { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? NotBefore { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public GlobalSecret GlobalSecret { get; set; } = null!;
}
