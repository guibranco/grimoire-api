using Grimoire.Core.Secrets;
using Xunit;

namespace Grimoire.Tests;

public class SecretVersionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class V(
        int version,
        bool enabled = true,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? expiresAt = null
    ) : ISecretVersionWindow
    {
        public int Version { get; } = version;
        public bool IsEnabled { get; } = enabled;
        public DateTimeOffset? NotBefore { get; } = notBefore;
        public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    }

    [Fact]
    public void Current_IsTheHighestValidVersion()
    {
        var v1 = new V(1);
        var v2 = new V(2, expiresAt: Now.AddDays(-1));
        var v3 = new V(3, notBefore: Now.AddDays(1));
        var v4 = new V(4, enabled: false);

        Assert.Same(v1, SecretVersionRules.Current(new[] { v1, v2, v3, v4 }, Now));
        Assert.Null(SecretVersionRules.Current(new[] { v2, v3, v4 }, Now));
    }

    [Fact]
    public void Status_ClassifiesEveryVersion()
    {
        var current = 3;
        Assert.Equal(
            SecretVersionStatus.Current,
            SecretVersionRules.Status(new V(3), current, Now)
        );
        Assert.Equal(SecretVersionStatus.Valid, SecretVersionRules.Status(new V(2), current, Now));
        Assert.Equal(
            SecretVersionStatus.Scheduled,
            SecretVersionRules.Status(new V(5, notBefore: Now.AddHours(1)), current, Now)
        );
        Assert.Equal(
            SecretVersionStatus.Expired,
            SecretVersionRules.Status(new V(1, expiresAt: Now.AddHours(-1)), current, Now)
        );
        Assert.Equal(
            SecretVersionStatus.Disabled,
            SecretVersionRules.Status(
                new V(4, enabled: false, expiresAt: Now.AddHours(-1)),
                current,
                Now
            )
        );
    }

    [Fact]
    public void VersionsToExpire_CapsOpenAndLaterExpiries_OnlyForEnabledVersions()
    {
        var expiresAt = Now.AddDays(7);
        var open = new V(1);
        var later = new V(2, expiresAt: Now.AddDays(30));
        var sooner = new V(3, expiresAt: Now.AddDays(2));
        var disabled = new V(4, enabled: false);

        var picked = SecretVersionRules.VersionsToExpire(
            new[] { open, later, sooner, disabled },
            expiresAt
        );

        Assert.Equal(new[] { open, later }, picked);
    }

    [Fact]
    public void WouldLeaveNoValidVersion_OnlyWhenTheLastValidVersionGoes()
    {
        var v1 = new V(1);
        var v2 = new V(2);
        IReadOnlyCollection<V> both = new[] { v1, v2 };
        IReadOnlyCollection<V> only = new[] { v2 };

        Assert.False(SecretVersionRules.WouldLeaveNoValidVersion(both, v2, false, null, null, Now));
        Assert.True(SecretVersionRules.WouldLeaveNoValidVersion(only, v2, false, null, null, Now));
        Assert.True(
            SecretVersionRules.WouldLeaveNoValidVersion(only, v2, true, null, Now.AddDays(-1), Now)
        );
        Assert.False(
            SecretVersionRules.WouldLeaveNoValidVersion(only, v2, true, null, Now.AddDays(1), Now)
        );
    }

    [Fact]
    public void WouldLeaveNoValidVersion_IsFalseWhenNothingIsValidAlready()
    {
        var expired = new V(1, expiresAt: Now.AddDays(-1));
        Assert.False(
            SecretVersionRules.WouldLeaveNoValidVersion(
                new[] { expired },
                expired,
                false,
                null,
                null,
                Now
            )
        );
    }
}
