using Grimoire.Core.Entities;

namespace Grimoire.Core.Publishing;

/// <summary>Where an environment (or the global layer) stands with publishing.</summary>
/// <param name="AutoPublish">Every change bumps the sentinel (<c>true</c>) or only a publish does.</param>
/// <param name="SentinelKey">The key clients watch.</param>
/// <param name="SentinelValue">Its current value; <c>null</c> before the first publish.</param>
/// <param name="LastPublishedAt">When the sentinel last changed; <c>null</c> before the first publish.</param>
/// <param name="PendingChangesSince">First change not yet published; <c>null</c> when clients are up to date.</param>
public record PublishingStatus(
    bool AutoPublish,
    string SentinelKey,
    string? SentinelValue,
    DateTimeOffset? LastPublishedAt,
    DateTimeOffset? PendingChangesSince
);

/// <summary>
/// Auto-publish settings and explicit publishing of configuration changes
/// (guibranco/grimoire-api#95).
/// </summary>
public interface IPublishingService
{
    Task<PublishingStatus> GetAsync(AppEnvironment environment, CancellationToken ct = default);

    /// <summary>Turning auto-publish on publishes any pending changes straight away.</summary>
    Task<PublishingStatus> SetAutoPublishAsync(
        AppEnvironment environment,
        bool autoPublish,
        CancellationToken ct = default
    );

    /// <summary>Bumps the environment's sentinel, so its clients reload.</summary>
    Task<PublishingStatus> PublishAsync(AppEnvironment environment, CancellationToken ct = default);

    Task<PublishingStatus> GetGlobalAsync(CancellationToken ct = default);

    /// <summary>Turning auto-publish on publishes any pending global changes straight away.</summary>
    Task<PublishingStatus> SetGlobalAutoPublishAsync(
        bool autoPublish,
        CancellationToken ct = default
    );

    /// <summary>Bumps <c>Global:Sentinel</c>, so every inheriting application's clients reload.</summary>
    Task<PublishingStatus> PublishGlobalAsync(CancellationToken ct = default);
}
