namespace Grimoire.Core.Entities;

/// <summary>
/// Remembers that a rotation reminder was delivered, so each (secret, environment, due date,
/// threshold) is announced once. The key never contains a secret value.
/// </summary>
public class RotationReminder
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}
