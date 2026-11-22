namespace Grimoire.Infrastructure.Audit;

/// <summary>Audit log settings (section <c>Audit</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Record audit events. Default <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Delete events older than this many days. <c>0</c> keeps them forever. Default 180.</summary>
    public int RetentionDays { get; init; } = 180;

    /// <summary>Events waiting to be written; beyond this, new events are dropped (and counted). Default 10,000.</summary>
    public int QueueCapacity { get; init; } = 10_000;
}
