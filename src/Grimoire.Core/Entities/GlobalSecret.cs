namespace Grimoire.Core.Entities;

/// <summary>A secret shared by every application that inherits globals.</summary>
public class GlobalSecret
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GlobalSecretVersion> Versions { get; set; } = [];
}
