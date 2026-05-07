namespace Grimoire.Core.Entities;

public class AppEnvironment
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Application Application { get; set; } = null!;
    public ICollection<SecretVersion> SecretVersions { get; set; } = [];
    public ICollection<ConfigurationEntry> ConfigurationEntries { get; set; } = [];
}
