namespace Grimoire.Core.Entities;

public class Secret
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Application Application { get; set; } = null!;
    public ICollection<SecretVersion> Versions { get; set; } = [];
}
