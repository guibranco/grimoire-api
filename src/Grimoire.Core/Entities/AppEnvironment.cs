namespace Grimoire.Core.Entities;

public class AppEnvironment
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Bump the <c>Sentinel</c> key on every configuration or secret change (default), or only
    /// when changes are published explicitly.
    /// </summary>
    public bool AutoPublish { get; set; } = true;

    /// <summary>First change not yet published while auto-publish is off; <c>null</c> when none.</summary>
    public DateTimeOffset? PendingChangesSince { get; set; }

    public Application Application { get; set; } = null!;
    public ICollection<SecretVersion> SecretVersions { get; set; } = [];
    public ICollection<ConfigurationEntry> ConfigurationEntries { get; set; } = [];
}
