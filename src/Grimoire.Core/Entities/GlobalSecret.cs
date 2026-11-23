namespace Grimoire.Core.Entities;

/// <summary>A secret shared by every application that inherits globals.</summary>
public class GlobalSecret
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Rotate at least this often: the current version is due this many days after it took
    /// effect (<c>NotBefore</c>, else <c>CreatedAt</c>). <c>null</c> means no rotation schedule.
    /// </summary>
    public int? RotationIntervalDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GlobalSecretVersion> Versions { get; set; } = [];
}
