namespace Grimoire.Core.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ApiKeyHash { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AppEnvironment> Environments { get; set; } = [];
    public ICollection<Secret> Secrets { get; set; } = [];
    public ICollection<ConfigurationEntry> ConfigurationEntries { get; set; } = [];
}
