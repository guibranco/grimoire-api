using System.Globalization;

namespace Grimoire.Core.Publishing;

/// <summary>
/// The sentinel keys clients watch to know when to reload configuration
/// (guibranco/grimoire-api#95). A client polls the sentinel with <c>If-None-Match</c> and
/// reloads everything only when its value changes. The value is bumped on every change while
/// auto-publish is on, or only on an explicit publish while it is off, so running clients
/// reload once after a batch of edits. Values themselves are served as soon as they are saved.
/// </summary>
public static class ConfigurationSentinel
{
    /// <summary>Per application and environment.</summary>
    public const string ApplicationKey = "Sentinel";

    /// <summary>
    /// One global sentinel, stored at the <c>*</c> layer so every inheriting application sees
    /// it, whichever environment-slug layer a global change targeted.
    /// </summary>
    public const string GlobalKey = "Global:Sentinel";

    /// <summary>
    /// Whether <paramref name="key"/> is a sentinel. Sentinels are served by the single-key
    /// endpoint (that is how clients poll them) but left out of lists, exports, diffs and
    /// dashboard counts, where they would only be noise.
    /// </summary>
    public static bool IsSentinel(string key) => key is ApplicationKey or GlobalKey;

    /// <summary>
    /// Why <paramref name="key"/> can't be written where it was asked, or <c>null</c> when it
    /// can. <c>Global:Sentinel</c> lives only in global configuration at the <c>*</c> layer, and
    /// <c>Sentinel</c> only in applications: anywhere else they would shadow the real sentinel,
    /// and clients would miss publishes.
    /// </summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="globalEnvironmentSlug">The global layer (<c>*</c> or a slug); <c>null</c> for an application.</param>
    public static string? ReservedKeyError(string key, string? globalEnvironmentSlug) =>
        (key, globalEnvironmentSlug) switch
        {
            (GlobalKey, null) => $"'{GlobalKey}' is managed by Grimoire for global configuration.",
            (ApplicationKey, not null) =>
                $"'{ApplicationKey}' is managed by Grimoire per application environment.",
            (GlobalKey, not "*") => $"'{GlobalKey}' can only be set for all environments (*).",
            _ => null,
        };

    /// <summary>
    /// A new value, strictly greater than <paramref name="current"/>: Unix milliseconds of
    /// <paramref name="now"/>, or <paramref name="current"/> + 1 when two bumps share a millisecond.
    /// </summary>
    public static string NextValue(string? current, DateTimeOffset now)
    {
        var next = now.ToUnixTimeMilliseconds();
        if (
            long.TryParse(
                current,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var previous
            )
            && previous >= next
        )
            next = previous + 1;
        return next.ToString(CultureInfo.InvariantCulture);
    }
}
