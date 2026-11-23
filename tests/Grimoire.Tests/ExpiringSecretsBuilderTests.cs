using Grimoire.Core.Dashboard;
using Grimoire.Core.Secrets;
using Xunit;

namespace Grimoire.Tests;

public class ExpiringSecretsBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid DeletedAppId = Guid.NewGuid();
    private static readonly Guid ProdId = Guid.NewGuid();
    private static readonly Guid StagingId = Guid.NewGuid();
    private static readonly Guid DbPassword = Guid.NewGuid();
    private static readonly Guid ApiKey = Guid.NewGuid();
    private static readonly Guid Smtp = Guid.NewGuid();

    private static SecretMetadataSnapshot Snapshot(
        IReadOnlyList<SecretVersionRow>? versions = null,
        IReadOnlyList<GlobalSecretVersionRow>? globalVersions = null
    ) =>
        new(
            [new ApplicationRow(AppId, "billing", "Billing", true, Now)],
            [
                new EnvironmentRow(ProdId, AppId, "production", "Production", Now),
                new EnvironmentRow(StagingId, AppId, "staging", "Staging", Now),
            ],
            [
                new SecretRow(DbPassword, AppId, "DB_PASSWORD", Now, RotationIntervalDays: 90),
                new SecretRow(ApiKey, AppId, "API_KEY", Now),
                // Its application is not in the snapshot (soft-deleted): never listed.
                new SecretRow(Guid.NewGuid(), DeletedAppId, "ORPHAN", Now),
            ],
            versions ?? [],
            [new GlobalSecretRow(Smtp, "SMTP_PASSWORD", Now, RotationIntervalDays: 30)],
            globalVersions ?? []
        );

    private static SecretVersionRow AppVersion(
        Guid secretId,
        Guid envId,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null
    ) => new(secretId, envId, true, expiresAt, null, 1, createdAt);

    private static GlobalSecretVersionRow GlobalVersion(
        string slug,
        DateTimeOffset createdAt,
        bool enabled = true
    ) => new(Smtp, slug, enabled, null, null, 1, createdAt);

    [Fact]
    public void ListsDueSecretsWithinTheWindow_SoonestFirst()
    {
        var items = ExpiringSecretsBuilder.Build(
            Snapshot(
                versions:
                [
                    AppVersion(DbPassword, ProdId, Now.AddDays(-80)), // rotation in 10 days
                    AppVersion(DbPassword, StagingId, Now.AddDays(-1)), // rotation in 89 days
                    AppVersion(ApiKey, ProdId, Now, expiresAt: Now.AddDays(3)),
                ]
            ),
            Now,
            Now.AddDays(30)
        );

        Assert.Collection(
            items,
            i =>
            {
                Assert.Equal(("API_KEY", "production"), (i.Secret, i.Environment));
                Assert.Equal(SecretDueReasons.Expiry, i.Reason);
            },
            i =>
            {
                Assert.Equal(("DB_PASSWORD", "production"), (i.Secret, i.Environment));
                Assert.Equal(SecretDueReasons.Rotation, i.Reason);
                Assert.Equal(Now.AddDays(10), i.DueAt);
                Assert.Equal("billing", i.Application);
                Assert.Equal(90, i.RotationIntervalDays);
                Assert.False(i.IsGlobal);
            }
        );
    }

    [Fact]
    public void OverdueSecrets_AreAlwaysIncluded()
    {
        var items = ExpiringSecretsBuilder.Build(
            Snapshot(versions: [AppVersion(DbPassword, ProdId, Now.AddDays(-100))]),
            Now,
            Now
        );

        var item = Assert.Single(items);
        Assert.Equal(Now.AddDays(-10), item.DueAt);
    }

    [Fact]
    public void GlobalSecrets_AreListedPerEnvironmentSlug()
    {
        var items = ExpiringSecretsBuilder.Build(
            Snapshot(globalVersions: [GlobalVersion("*", Now.AddDays(-25))]),
            Now,
            Now.AddDays(30)
        );

        var item = Assert.Single(items);
        Assert.True(item.IsGlobal);
        Assert.Null(item.Application);
        Assert.Equal(("SMTP_PASSWORD", "*"), (item.Secret, item.Environment));
        Assert.Equal(Now.AddDays(5), item.DueAt);
    }

    [Fact]
    public void GlobalSlugWithoutValidVersion_IsSkippedWhenTheWildcardCoversIt()
    {
        var items = ExpiringSecretsBuilder.Build(
            Snapshot(
                globalVersions:
                [
                    GlobalVersion("*", Now.AddDays(-1)),
                    GlobalVersion("production", Now.AddDays(-25), enabled: false),
                ]
            ),
            Now,
            Now.AddDays(60)
        );

        Assert.Equal("*", Assert.Single(items).Environment);
    }
}
