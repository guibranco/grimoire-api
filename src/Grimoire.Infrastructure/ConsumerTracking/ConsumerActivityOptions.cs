namespace Grimoire.Infrastructure.ConsumerTracking;

/// <summary>Settings for consumer last-seen tracking (section <c>ConsumerActivity</c>).</summary>
public sealed class ConsumerActivityOptions
{
    public const string SectionName = "ConsumerActivity";

    /// <summary>How often buffered reads are written to the database. Default 60 seconds.</summary>
    public int FlushIntervalSeconds { get; init; } = 60;
}
