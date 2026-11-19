using Grimoire.Core.Dashboard;
using Xunit;

namespace Grimoire.Tests;

public class DashboardBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid ProdId = Guid.NewGuid();
    private static readonly Guid StagingId = Guid.NewGuid();
    private static readonly Guid SecretId = Guid.NewGuid();

    private static DashboardSnapshot Snapshot(
        IReadOnlyList<SecretVersionRow>? versions = null,
        IReadOnlyList<ConfigurationRow>? configs = null,
        IReadOnlyList<GlobalSecretRow>? globalSecrets = null,
        IReadOnlyList<GlobalSecretVersionRow>? globalVersions = null,
        IReadOnlyList<GlobalConfigurationRow>? globalConfigs = null,
        IReadOnlyDictionary<(Guid, Guid), DateTimeOffset>? lastSeen = null,
        bool withSecret = true
    ) =>
        new(
            [new ApplicationRow(AppId, "loop-requests", "Loop Requests", true, Now.AddDays(-30))],
            [
                new EnvironmentRow(ProdId, AppId, "production", "Production", Now.AddDays(-30)),
                new EnvironmentRow(StagingId, AppId, "staging", "Staging", Now.AddDays(-30)),
            ],
            withSecret ? [new SecretRow(SecretId, AppId, "DB_PASSWORD", Now.AddDays(-20))] : [],
            versions ?? [],
            configs ?? [],
            globalSecrets ?? [],
            globalVersions ?? [],
            globalConfigs ?? [],
            lastSeen
        );

    private static SecretVersionRow Version(
        Guid envId,
        int version,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? notBefore = null,
        bool enabled = true
    ) => new(SecretId, envId, enabled, expiresAt, notBefore, version, Now.AddDays(-10 + version));

    private static DashboardSummary Build(DashboardSnapshot snapshot) =>
        DashboardBuilder.Build(snapshot, Now, expiringWithinDays: 14, staleAfterDays: 7);

    private static AttentionItem? For(DashboardSummary s, string env, string kind) =>
        s.Attention.SingleOrDefault(a => a.Environment == env && a.Kind == kind);

    [Fact]
    public void ActiveVersionWithoutExpiry_RaisesNothing()
    {
        var summary = Build(Snapshot(versions: [Version(ProdId, 1), Version(StagingId, 1)]));

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void ActiveVersionExpiringWithinThreshold_IsWarning()
    {
        var due = Now.AddDays(9);
        var summary = Build(
            Snapshot(versions: [Version(ProdId, 1, expiresAt: due), Version(StagingId, 1)])
        );

        var item = Assert.Single(summary.Attention);
        Assert.Equal(AttentionKinds.SecretExpiring, item.Kind);
        Assert.Equal(AttentionSeverity.Warning, item.Severity);
        Assert.Equal("loop-requests", item.Application);
        Assert.Equal("production", item.Environment);
        Assert.Equal("DB_PASSWORD", item.Target);
        Assert.Equal(due, item.DueAt);
    }

    [Fact]
    public void ActiveVersionExpiringAfterThreshold_RaisesNothing()
    {
        var summary = Build(
            Snapshot(
                versions: [Version(ProdId, 1, expiresAt: Now.AddDays(30)), Version(StagingId, 1)]
            )
        );

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void ExpiringThresholdIsConfigurable()
    {
        var snapshot = Snapshot(
            versions: [Version(ProdId, 1, expiresAt: Now.AddDays(30)), Version(StagingId, 1)]
        );

        var summary = DashboardBuilder.Build(
            snapshot,
            Now,
            expiringWithinDays: 45,
            staleAfterDays: 7
        );

        Assert.NotNull(For(summary, "production", AttentionKinds.SecretExpiring));
    }

    [Fact]
    public void LatestVersionExpired_WithNoOtherValidVersion_IsError()
    {
        var expired = Now.AddDays(-1);
        var summary = Build(
            Snapshot(versions: [Version(ProdId, 1, expiresAt: expired), Version(StagingId, 1)])
        );

        var item = Assert.Single(summary.Attention);
        Assert.Equal(AttentionKinds.SecretExpired, item.Kind);
        Assert.Equal(AttentionSeverity.Error, item.Severity);
        Assert.Equal(expired, item.DueAt);
    }

    [Fact]
    public void LatestVersionExpired_ButOlderVersionStillValid_IsNotExpired()
    {
        var summary = Build(
            Snapshot(
                versions:
                [
                    Version(ProdId, 1),
                    Version(ProdId, 2, expiresAt: Now.AddDays(-1)),
                    Version(StagingId, 1),
                ]
            )
        );

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void DisabledOrNotYetValidVersions_AreNoValidVersion()
    {
        var summary = Build(
            Snapshot(
                versions:
                [
                    Version(ProdId, 1, enabled: false),
                    Version(StagingId, 1, notBefore: Now.AddDays(2)),
                ]
            )
        );

        Assert.Equal(2, summary.Attention.Count);
        Assert.All(summary.Attention, a =>
        {
            Assert.Equal(AttentionKinds.SecretNoValidVersion, a.Kind);
            Assert.Equal(AttentionSeverity.Error, a.Severity);
            Assert.Null(a.DueAt);
        });
    }

    [Fact]
    public void EnvironmentTheSecretWasNeverSetIn_IsNotFlagged()
    {
        // Every application gets a "local" environment that often never receives values.
        var summary = Build(Snapshot(versions: [Version(ProdId, 1)]));

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void SecretWithNoVersionsAnywhere_IsNoValidVersionWithoutEnvironment()
    {
        var summary = Build(Snapshot());

        var item = Assert.Single(summary.Attention);
        Assert.Equal(AttentionKinds.SecretNoValidVersion, item.Kind);
        Assert.Equal("DB_PASSWORD", item.Target);
        Assert.Null(item.Environment);
    }

    [Fact]
    public void BlankConfigurationValues_AreInfo()
    {
        var summary = Build(
            Snapshot(
                configs:
                [
                    new ConfigurationRow(AppId, ProdId, "SMTP_HOST", true, Now),
                    new ConfigurationRow(AppId, ProdId, "SMTP_PORT", false, Now),
                ],
                withSecret: false
            )
        );

        var item = Assert.Single(summary.Attention);
        Assert.Equal(AttentionKinds.ConfigEmptyValue, item.Kind);
        Assert.Equal(AttentionSeverity.Info, item.Severity);
        Assert.Equal("SMTP_HOST", item.Target);
    }

    [Fact]
    public void ConsumerActivityNotTracked_SkipsConsumerRules_AndLastSeenIsNull()
    {
        var summary = Build(
            Snapshot(versions: [Version(ProdId, 1), Version(StagingId, 1)], lastSeen: null)
        );

        Assert.Empty(summary.Attention);
        var app = Assert.Single(summary.Applications);
        Assert.Null(app.LastSeenAt);
        Assert.All(app.Environments, e => Assert.Null(e.LastSeenAt));
    }

    [Fact]
    public void ConsumerRules_FlagStaleAndNeverSeen_AndReportLastSeen()
    {
        var staleSeen = Now.AddDays(-8);
        var summary = Build(
            Snapshot(
                versions: [Version(ProdId, 1), Version(StagingId, 1)],
                lastSeen: new Dictionary<(Guid, Guid), DateTimeOffset>
                {
                    [(AppId, ProdId)] = staleSeen,
                }
            )
        );

        var stale = For(summary, "production", AttentionKinds.ConsumerStale);
        Assert.NotNull(stale);
        Assert.Equal(AttentionSeverity.Warning, stale.Severity);
        Assert.Equal(staleSeen, stale.LastSeenAt);

        var never = For(summary, "staging", AttentionKinds.ConsumerNeverSeen);
        Assert.NotNull(never);
        Assert.Equal(AttentionSeverity.Info, never.Severity);

        var app = Assert.Single(summary.Applications);
        Assert.Equal(staleSeen, app.LastSeenAt);
        Assert.Equal(staleSeen, app.Environments.Single(e => e.Slug == "production").LastSeenAt);
    }

    [Fact]
    public void RecentlySeenConsumer_IsNotStale()
    {
        var summary = Build(
            Snapshot(
                versions: [Version(ProdId, 1), Version(StagingId, 1)],
                lastSeen: new Dictionary<(Guid, Guid), DateTimeOffset>
                {
                    [(AppId, ProdId)] = Now.AddHours(-1),
                    [(AppId, StagingId)] = Now.AddDays(-6),
                }
            )
        );

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void EnvironmentWithNothingToRead_IsNotNeverSeen()
    {
        var summary = Build(
            Snapshot(lastSeen: new Dictionary<(Guid, Guid), DateTimeOffset>(), withSecret: false)
        );

        Assert.Empty(summary.Attention);
    }

    [Fact]
    public void Counts_AndLastChangedAt_AreAggregated()
    {
        var configChanged = Now.AddHours(-3);
        var summary = Build(
            Snapshot(
                versions: [Version(ProdId, 1), Version(ProdId, 2), Version(StagingId, 1)],
                configs:
                [
                    new ConfigurationRow(AppId, ProdId, "A", false, Now.AddDays(-5)),
                    new ConfigurationRow(AppId, ProdId, "B", false, configChanged),
                    new ConfigurationRow(AppId, StagingId, "A", false, Now.AddDays(-5)),
                ],
                globalSecrets: [new GlobalSecretRow(Guid.NewGuid(), "AMQP", Now.AddDays(-2))],
                globalConfigs: [new GlobalConfigurationRow("*", "TZ", false, Now.AddDays(-1))]
            )
        );

        Assert.Equal(new DashboardTotals(1, 2, 1, 3), summary.Totals);
        var app = Assert.Single(summary.Applications);
        Assert.Equal(1, app.SecretCount);
        Assert.Equal(3, app.ConfigurationCount);
        Assert.Equal(configChanged, app.LastChangedAt);

        var prod = app.Environments.Single(e => e.Slug == "production");
        Assert.Equal(1, prod.SecretCount);
        Assert.Equal(2, prod.ConfigurationCount);
        Assert.Equal(configChanged, prod.LastChangedAt);

        Assert.Equal(1, summary.Global.SecretCount);
        Assert.Equal(1, summary.Global.ConfigurationCount);
        Assert.Equal(Now.AddDays(-1), summary.Global.LastChangedAt);
    }

    [Fact]
    public void GlobalSection_IsEmptyWhenNothingIsGlobal()
    {
        var summary = Build(Snapshot(withSecret: false));

        Assert.Equal(new DashboardGlobal(0, 0, null), summary.Global);
    }

    [Fact]
    public void GlobalSecrets_FollowTheSameRules_WithWildcardFallback()
    {
        var expiring = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var coveredByWildcard = Guid.NewGuid();
        var empty = Guid.NewGuid();

        var summary = Build(
            Snapshot(
                globalSecrets:
                [
                    new GlobalSecretRow(expiring, "EXPIRING", Now),
                    new GlobalSecretRow(expired, "EXPIRED", Now),
                    new GlobalSecretRow(coveredByWildcard, "COVERED", Now),
                    new GlobalSecretRow(empty, "EMPTY", Now),
                ],
                globalVersions:
                [
                    new GlobalSecretVersionRow(expiring, "*", true, Now.AddDays(3), null, 1, Now),
                    new GlobalSecretVersionRow(
                        expired,
                        "production",
                        true,
                        Now.AddDays(-3),
                        null,
                        1,
                        Now
                    ),
                    new GlobalSecretVersionRow(coveredByWildcard, "*", true, null, null, 1, Now),
                    new GlobalSecretVersionRow(
                        coveredByWildcard,
                        "production",
                        false,
                        null,
                        null,
                        1,
                        Now
                    ),
                ],
                globalConfigs: [new GlobalConfigurationRow("production", "BLANK", true, Now)],
                withSecret: false
            )
        );

        Assert.All(summary.Attention, a => Assert.Null(a.Application));
        Assert.Equal(4, summary.Attention.Count);
        Assert.Contains(summary.Attention, a =>
            a is { Kind: AttentionKinds.SecretExpiring, Target: "EXPIRING", Environment: "*" }
        );
        Assert.Contains(summary.Attention, a =>
            a
                is {
                    Kind: AttentionKinds.SecretExpired,
                    Target: "EXPIRED",
                    Environment: "production"
                }
        );
        Assert.Contains(summary.Attention, a =>
            a is { Kind: AttentionKinds.SecretNoValidVersion, Target: "EMPTY", Environment: null }
        );
        Assert.Contains(summary.Attention, a =>
            a is { Kind: AttentionKinds.ConfigEmptyValue, Target: "BLANK" }
        );
        Assert.DoesNotContain(summary.Attention, a => a.Target == "COVERED");
    }

    [Fact]
    public void Attention_IsOrderedBySeverity()
    {
        var otherSecret = Guid.NewGuid();
        var snapshot = Snapshot(
            versions: [Version(ProdId, 1, expiresAt: Now.AddDays(2))],
            configs: [new ConfigurationRow(AppId, ProdId, "BLANK", true, Now)]
        ) with
        {
            Secrets =
            [
                new SecretRow(SecretId, AppId, "DB_PASSWORD", Now),
                new SecretRow(otherSecret, AppId, "UNSET", Now),
            ],
        };

        var summary = Build(snapshot);

        Assert.Equal(
            [AttentionSeverity.Error, AttentionSeverity.Warning, AttentionSeverity.Info],
            summary.Attention.Select(a => a.Severity)
        );
    }
}
