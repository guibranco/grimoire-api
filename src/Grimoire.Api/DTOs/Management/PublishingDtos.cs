using Grimoire.Core.Publishing;

namespace Grimoire.Api.DTOs.Management;

public record UpdatePublishingRequest(bool AutoPublish);

/// <param name="AutoPublish">Every change bumps the sentinel (<c>true</c>) or only a publish does.</param>
/// <param name="SentinelKey">The configuration key clients watch: <c>Sentinel</c> or <c>Global:Sentinel</c>.</param>
/// <param name="SentinelValue">Its current value; <c>null</c> before the first publish.</param>
/// <param name="LastPublishedAt">When the sentinel last changed.</param>
/// <param name="PendingChangesSince">First change clients haven't been told about; <c>null</c> when up to date.</param>
/// <param name="HasPendingChanges">Whether there are unpublished changes.</param>
public record PublishingResponse(
    bool AutoPublish,
    string SentinelKey,
    string? SentinelValue,
    DateTimeOffset? LastPublishedAt,
    DateTimeOffset? PendingChangesSince,
    bool HasPendingChanges
)
{
    public static PublishingResponse From(PublishingStatus s) =>
        new(
            s.AutoPublish,
            s.SentinelKey,
            s.SentinelValue,
            s.LastPublishedAt,
            s.PendingChangesSince,
            s.PendingChangesSince is not null
        );
}
