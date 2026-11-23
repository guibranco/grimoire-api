namespace Grimoire.Api.DTOs.Management;

/// <summary>Value-free summary for the management UI home page. Never contains secret or configuration values.</summary>
/// <param name="FeatureFlags">Application and global flags, and how many are on in at least one environment.</param>
/// <param name="RecentActivity">Latest changes from the audit log (consumer reads excluded); <c>null</c> when auditing is disabled.</param>
public record DashboardResponse(
    DateTimeOffset GeneratedAt,
    DashboardTotalsResponse Totals,
    IReadOnlyList<DashboardApplicationResponse> Applications,
    DashboardGlobalResponse Global,
    DashboardFeatureFlagsResponse FeatureFlags,
    IReadOnlyList<DashboardActivityResponse>? RecentActivity,
    IReadOnlyList<AttentionItemResponse> Attention
);

/// <param name="Total">Every flag, application and global.</param>
/// <param name="Enabled">Flags on in at least one environment (for a global flag, including <c>*</c>).</param>
public record DashboardFeatureFlagsResponse(int Total, int Enabled);

public record DashboardTotalsResponse(
    int Applications,
    int Environments,
    int Secrets,
    int Configurations
);

/// <param name="SecretCount">Secrets defined by the application (globals excluded).</param>
/// <param name="LastChangedAt">Latest change to the application, its environments, secrets, secret versions or configuration.</param>
/// <param name="LastSeenAt">Latest consumer read across environments; <c>null</c> when never seen or not tracked.</param>
public record DashboardApplicationResponse(
    string Slug,
    string Name,
    bool InheritGlobals,
    IReadOnlyList<DashboardEnvironmentResponse> Environments,
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset LastChangedAt,
    DateTimeOffset? LastSeenAt
);

/// <param name="SecretCount">Application secrets with at least one version in this environment.</param>
public record DashboardEnvironmentResponse(
    string Slug,
    string Name,
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset LastChangedAt,
    DateTimeOffset? LastSeenAt
);

public record DashboardGlobalResponse(
    int SecretCount,
    int ConfigurationCount,
    DateTimeOffset? LastChangedAt
);

/// <param name="Kind">
/// <c>secret-expired</c>, <c>secret-expiring</c>, <c>secret-no-valid-version</c>,
/// <c>consumer-stale</c>, <c>consumer-never-seen</c> or <c>config-empty-value</c>.
/// </param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="Application">Application slug, or <c>null</c> for global secrets and configuration.</param>
/// <param name="Environment">Environment slug (<c>*</c> for global values targeting every environment).</param>
/// <param name="Target">Secret name or configuration key; never a value.</param>
/// <param name="DueAt">Expiry of the secret version, for the secret rules.</param>
/// <param name="LastSeenAt">Last consumer read, for <c>consumer-stale</c>.</param>
public record AttentionItemResponse(
    string Kind,
    string Severity,
    string? Application,
    string? Environment,
    string? Target,
    DateTimeOffset? DueAt,
    DateTimeOffset? LastSeenAt
);
