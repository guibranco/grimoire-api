namespace Grimoire.Consumer.Models;

public class GrimoireSecretProperties
{
    public bool Enabled { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public DateTimeOffset? NotBefore { get; init; }
    public int Version { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset? UpdatedOn { get; init; }
}

public class GrimoireSecret
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public GrimoireSecretProperties Properties { get; init; } = new();
}
