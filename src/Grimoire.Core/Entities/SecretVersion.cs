namespace Grimoire.Core.Entities;

public class SecretVersion
{
    public Guid Id { get; set; }
    public Guid SecretId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string EncryptedValue { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? NotBefore { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Secret Secret { get; set; } = null!;
    public AppEnvironment Environment { get; set; } = null!;
}
