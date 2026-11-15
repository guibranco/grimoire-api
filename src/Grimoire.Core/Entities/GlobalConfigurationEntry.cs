namespace Grimoire.Core.Entities;

/// <summary>
/// A configuration entry shared by every application that inherits globals.
/// Targets environments by slug, or all environments with <see cref="AllEnvironments"/>.
/// </summary>
public class GlobalConfigurationEntry
{
    /// <summary>Sentinel slug meaning "every environment of every application".</summary>
    public const string AllEnvironments = "*";

    public Guid Id { get; set; }
    public string EnvironmentSlug { get; set; } = AllEnvironments;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
