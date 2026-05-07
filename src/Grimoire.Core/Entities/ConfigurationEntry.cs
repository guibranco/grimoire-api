namespace Grimoire.Core.Entities;

public class ConfigurationEntry
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Application Application { get; set; } = null!;
    public AppEnvironment Environment { get; set; } = null!;
}
