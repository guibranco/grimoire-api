namespace Grimoire.Core.Entities;

/// <summary>
/// Publishing settings for global configuration (a single row, <see cref="SingletonId"/>).
/// When no row exists, globals auto-publish.
/// </summary>
public class GlobalPublishing
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Bump <c>Global:Sentinel</c> on every global change (default) or only on publish.</summary>
    public bool AutoPublish { get; set; } = true;

    /// <summary>First change not yet published while auto-publish is off; <c>null</c> when none.</summary>
    public DateTimeOffset? PendingChangesSince { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
