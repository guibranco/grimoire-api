namespace Grimoire.Core.Entities;

public class Secret
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Rotate at least this often: the current version is due this many days after it took
    /// effect (<c>NotBefore</c>, else <c>CreatedAt</c>). <c>null</c> means no rotation schedule.
    /// </summary>
    public int? RotationIntervalDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Application Application { get; set; } = null!;
    public ICollection<SecretVersion> Versions { get; set; } = [];
}
