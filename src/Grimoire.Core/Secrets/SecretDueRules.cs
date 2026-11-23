namespace Grimoire.Core.Secrets;

/// <summary>Why a secret needs attention.</summary>
public static class SecretDueReasons
{
    /// <summary>The current version has an expiry date.</summary>
    public const string Expiry = "expiry";

    /// <summary>The current version is older than the secret's rotation interval allows.</summary>
    public const string Rotation = "rotation";

    /// <summary>No version is valid any more because the latest one expired.</summary>
    public const string Expired = "expired";
}

/// <param name="Version">The version the due date belongs to.</param>
public sealed record SecretDue(string Reason, DateTimeOffset DueAt, int Version);

/// <summary>
/// When a secret (in one environment) next needs rotating: the earlier of the current version's
/// expiry and its rotation date. A newer version already scheduled to take over before that date
/// counts as the rotation being planned, so nothing is due.
/// </summary>
public static class SecretDueRules
{
    public const int MaxRotationIntervalDays = 3650;

    public static SecretDue? Evaluate<T>(
        IReadOnlyCollection<T> versions,
        int? rotationIntervalDays,
        DateTimeOffset now
    )
        where T : class, ISecretVersionRecord
    {
        var current = SecretVersionRules.Current(versions, now);
        if (current is null)
        {
            var latest = versions
                .Where(v => v.IsEnabled)
                .OrderByDescending(v => v.Version)
                .FirstOrDefault();
            return latest?.ExpiresAt is { } expiredAt && expiredAt <= now
                ? new SecretDue(SecretDueReasons.Expired, expiredAt, latest.Version)
                : null;
        }

        var rotationAt = rotationIntervalDays is { } days
            ? (current.NotBefore ?? current.CreatedAt).AddDays(days)
            : (DateTimeOffset?)null;

        SecretDue? due = (current.ExpiresAt, rotationAt) switch
        {
            ({ } e, { } r) when r < e => new SecretDue(
                SecretDueReasons.Rotation,
                r,
                current.Version
            ),
            ({ } e, _) => new SecretDue(SecretDueReasons.Expiry, e, current.Version),
            (null, { } r) => new SecretDue(SecretDueReasons.Rotation, r, current.Version),
            _ => null,
        };

        return due is not null && HasScheduledReplacement(versions, current, due.DueAt)
            ? null
            : due;
    }

    /// <summary>
    /// A newer enabled version that starts by <paramref name="dueAt"/> and is still valid then.
    /// </summary>
    private static bool HasScheduledReplacement<T>(
        IEnumerable<T> versions,
        T current,
        DateTimeOffset dueAt
    )
        where T : class, ISecretVersionRecord =>
        versions.Any(v =>
            v.IsEnabled
            && v.Version > current.Version
            && v.NotBefore is { } startsAt
            && startsAt <= dueAt
            && (v.ExpiresAt is null || v.ExpiresAt > dueAt)
        );

    /// <summary>
    /// The tightest reminder threshold (in days) that <paramref name="dueAt"/> has crossed:
    /// 0 once it is due or overdue, otherwise the smallest threshold with
    /// <c>dueAt &lt;= now + threshold</c>; <c>null</c> when it is further away than all of them.
    /// </summary>
    public static int? CrossedThreshold(
        DateTimeOffset dueAt,
        DateTimeOffset now,
        IEnumerable<int> thresholds
    )
    {
        if (dueAt <= now)
            return 0;
        int? crossed = null;
        foreach (var days in thresholds.Where(d => d > 0))
            if (dueAt <= now.AddDays(days) && (crossed is null || days < crossed))
                crossed = days;
        return crossed;
    }

    /// <summary>Whole calendar days (UTC) from <paramref name="now"/> to <paramref name="dueAt"/>; negative when overdue.</summary>
    public static int DaysRemaining(DateTimeOffset dueAt, DateTimeOffset now) =>
        (dueAt.UtcDateTime.Date - now.UtcDateTime.Date).Days;
}
