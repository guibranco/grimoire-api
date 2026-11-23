using Grimoire.Core.Secrets;

namespace Grimoire.Core.Dashboard;

/// <summary>
/// Thresholds for the dashboard attention rules. Defaults come from the <c>Dashboard</c>
/// configuration section and can be overridden per request.
/// </summary>
public sealed record DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>An active secret version expiring within this many days is flagged.</summary>
    public int ExpiringWithinDays { get; init; } = 14;

    /// <summary>A consumer not seen for more than this many days is flagged as stale.</summary>
    public int StaleAfterDays { get; init; } = 7;

    /// <summary>How long a computed summary is reused. 0 disables caching.</summary>
    public int CacheSeconds { get; init; } = 15;
}

/// <summary>Attention item kinds. Kept as constants so the API contract is greppable.</summary>
public static class AttentionKinds
{
    public const string SecretExpired = "secret-expired";
    public const string SecretExpiring = "secret-expiring";
    public const string SecretRotationDue = "secret-rotation-due";
    public const string SecretNoValidVersion = "secret-no-valid-version";
    public const string ConsumerStale = "consumer-stale";
    public const string ConsumerNeverSeen = "consumer-never-seen";
    public const string ConfigEmptyValue = "config-empty-value";
}

public enum AttentionSeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>Aggregate, value-free summary of the whole estate.</summary>
public sealed record DashboardSummary(
    DateTimeOffset GeneratedAt,
    DashboardTotals Totals,
    IReadOnlyList<DashboardApplication> Applications,
    DashboardGlobal Global,
    IReadOnlyList<AttentionItem> Attention
);

public sealed record DashboardTotals(
    int Applications,
    int Environments,
    int Secrets,
    int Configurations
);

public sealed record DashboardApplication(
    string Slug,
    string Name,
    bool InheritGlobals,
    IReadOnlyList<DashboardEnvironment> Environments,
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset LastChangedAt,
    DateTimeOffset? LastSeenAt
);

public sealed record DashboardEnvironment(
    string Slug,
    string Name,
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset LastChangedAt,
    DateTimeOffset? LastSeenAt
);

public sealed record DashboardGlobal(
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset? LastChangedAt
);

/// <summary>
/// Something an operator should look at. <see cref="Application"/> is <c>null</c> for global
/// secrets and configuration. <see cref="Target"/> is a secret name or configuration key, never a value.
/// </summary>
public sealed record AttentionItem(
    string Kind,
    AttentionSeverity Severity,
    string? Application,
    string? Environment,
    string? Target,
    DateTimeOffset? DueAt = null,
    DateTimeOffset? LastSeenAt = null
);

// ---- Value-free projections the builder works from -------------------------------------

public sealed record ApplicationRow(
    Guid Id,
    string Slug,
    string Name,
    bool InheritGlobals,
    DateTimeOffset UpdatedAt
);

public sealed record EnvironmentRow(
    Guid Id,
    Guid ApplicationId,
    string Slug,
    string Name,
    DateTimeOffset UpdatedAt
);

public sealed record SecretRow(
    Guid Id,
    Guid ApplicationId,
    string Name,
    DateTimeOffset UpdatedAt,
    int? RotationIntervalDays = null
);

/// <summary>Version metadata only; the encrypted value is never projected.</summary>
public sealed record SecretVersionRow(
    Guid SecretId,
    Guid EnvironmentId,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NotBefore,
    int Version,
    DateTimeOffset CreatedAt
) : ISecretVersionRecord;

/// <summary>Configuration metadata only; the value is reduced to an "is blank" flag in SQL.</summary>
public sealed record ConfigurationRow(
    Guid ApplicationId,
    Guid EnvironmentId,
    string Key,
    bool IsBlank,
    DateTimeOffset UpdatedAt
);

public sealed record GlobalSecretRow(
    Guid Id,
    string Name,
    DateTimeOffset UpdatedAt,
    int? RotationIntervalDays = null
);

public sealed record GlobalSecretVersionRow(
    Guid GlobalSecretId,
    string EnvironmentSlug,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NotBefore,
    int Version,
    DateTimeOffset CreatedAt
) : ISecretVersionRecord;

public sealed record GlobalConfigurationRow(
    string EnvironmentSlug,
    string Key,
    bool IsBlank,
    DateTimeOffset UpdatedAt
);

/// <summary>Everything the dashboard needs, already projected away from values.</summary>
/// <param name="LastSeen">
/// Last successful consumer read per (application, environment), or <c>null</c> when consumer
/// activity is not tracked. When <c>null</c>, <c>lastSeenAt</c> is <c>null</c> everywhere and the
/// consumer rules are skipped.
/// </param>
public sealed record DashboardSnapshot(
    IReadOnlyList<ApplicationRow> Applications,
    IReadOnlyList<EnvironmentRow> Environments,
    IReadOnlyList<SecretRow> Secrets,
    IReadOnlyList<SecretVersionRow> SecretVersions,
    IReadOnlyList<ConfigurationRow> Configurations,
    IReadOnlyList<GlobalSecretRow> GlobalSecrets,
    IReadOnlyList<GlobalSecretVersionRow> GlobalSecretVersions,
    IReadOnlyList<GlobalConfigurationRow> GlobalConfigurations,
    IReadOnlyDictionary<(Guid ApplicationId, Guid EnvironmentId), DateTimeOffset>? LastSeen
);
