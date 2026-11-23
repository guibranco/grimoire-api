namespace Grimoire.Core.Secrets;

/// <summary>The fields that decide whether a secret version can be served.</summary>
public interface ISecretVersionWindow
{
    int Version { get; }
    bool IsEnabled { get; }
    DateTimeOffset? NotBefore { get; }
    DateTimeOffset? ExpiresAt { get; }
}

/// <summary>A stored secret version whose window can be edited (the value never changes).</summary>
public interface IEditableSecretVersion : ISecretVersionWindow
{
    Guid Id { get; }
    DateTimeOffset CreatedAt { get; }
    void SetWindow(bool isEnabled, DateTimeOffset? notBefore, DateTimeOffset? expiresAt);
}

/// <summary>Where a version stands right now.</summary>
public static class SecretVersionStatus
{
    /// <summary>The version consumers get.</summary>
    public const string Current = "current";

    /// <summary>Valid, but a newer valid version is served instead.</summary>
    public const string Valid = "valid";

    /// <summary>Enabled, with a <c>notBefore</c> still in the future.</summary>
    public const string Scheduled = "scheduled";

    public const string Expired = "expired";
    public const string Disabled = "disabled";
}

/// <summary>
/// Key Vault-style resolution: the highest version that is enabled and inside its
/// <c>notBefore</c>/<c>expiresAt</c> window is served. Shared by the repositories, the
/// management API and the dashboard so they always agree.
/// </summary>
public static class SecretVersionRules
{
    public const int MaxGraceDays = 3650;

    public static bool IsValid(ISecretVersionWindow v, DateTimeOffset now) =>
        v.IsEnabled
        && (v.NotBefore is null || v.NotBefore <= now)
        && (v.ExpiresAt is null || v.ExpiresAt >= now);

    /// <summary>The version consumers get now, or <c>null</c> when none is valid.</summary>
    public static T? Current<T>(IEnumerable<T> versions, DateTimeOffset now)
        where T : class, ISecretVersionWindow =>
        versions.Where(v => IsValid(v, now)).OrderByDescending(v => v.Version).FirstOrDefault();

    public static string Status(ISecretVersionWindow v, int? currentVersion, DateTimeOffset now)
    {
        if (!v.IsEnabled)
            return SecretVersionStatus.Disabled;
        if (v.ExpiresAt is { } expires && expires < now)
            return SecretVersionStatus.Expired;
        if (v.NotBefore is { } notBefore && notBefore > now)
            return SecretVersionStatus.Scheduled;
        return v.Version == currentVersion
            ? SecretVersionStatus.Current
            : SecretVersionStatus.Valid;
    }

    /// <summary>
    /// When a new version is added with a grace period: every other enabled version with no
    /// expiry, or a later one, should expire at <c>now + graceDays</c>.
    /// </summary>
    /// <returns>The versions to give <paramref name="expiresAt"/>.</returns>
    public static IReadOnlyList<T> VersionsToExpire<T>(
        IEnumerable<T> others,
        DateTimeOffset expiresAt
    )
        where T : ISecretVersionWindow =>
        others.Where(v => v.IsEnabled && (v.ExpiresAt is null || v.ExpiresAt > expiresAt)).ToList();

    /// <summary>
    /// <c>true</c> when changing <paramref name="target"/> to the given window would leave the
    /// secret with no valid version although it has one now.
    /// </summary>
    public static bool WouldLeaveNoValidVersion<T>(
        IReadOnlyCollection<T> versions,
        T target,
        bool isEnabled,
        DateTimeOffset? notBefore,
        DateTimeOffset? expiresAt,
        DateTimeOffset now
    )
        where T : class, ISecretVersionWindow
    {
        if (Current(versions, now) is null)
            return false;
        var after = versions
            .Select(v =>
                ReferenceEquals(v, target)
                    ? new Window(v.Version, isEnabled, notBefore, expiresAt)
                    : new Window(v.Version, v.IsEnabled, v.NotBefore, v.ExpiresAt)
            )
            .ToList();
        return Current(after, now) is null;
    }

    private sealed record Window(
        int Version,
        bool IsEnabled,
        DateTimeOffset? NotBefore,
        DateTimeOffset? ExpiresAt
    ) : ISecretVersionWindow;
}
