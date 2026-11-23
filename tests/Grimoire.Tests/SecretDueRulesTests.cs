using Grimoire.Core.Dashboard;
using Grimoire.Core.Secrets;
using Xunit;

namespace Grimoire.Tests;

public class SecretDueRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static SecretVersionRow V(
        int version,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? notBefore = null,
        bool enabled = true
    ) =>
        new(
            Guid.Empty,
            Guid.Empty,
            enabled,
            expiresAt,
            notBefore,
            version,
            createdAt ?? Now.AddDays(-1)
        );

    [Fact]
    public void NoExpiryAndNoInterval_IsNeverDue() =>
        Assert.Null(SecretDueRules.Evaluate([V(1)], null, Now));

    [Fact]
    public void Expiry_IsDueOnTheCurrentVersionsExpiry()
    {
        var due = SecretDueRules.Evaluate([V(1, expiresAt: Now.AddDays(5))], null, Now);

        Assert.Equal(new SecretDue(SecretDueReasons.Expiry, Now.AddDays(5), 1), due);
    }

    [Fact]
    public void Rotation_CountsFromCreatedAt()
    {
        var due = SecretDueRules.Evaluate([V(1, createdAt: Now.AddDays(-80))], 90, Now);

        Assert.Equal(new SecretDue(SecretDueReasons.Rotation, Now.AddDays(10), 1), due);
    }

    [Fact]
    public void Rotation_CountsFromNotBeforeWhenSet()
    {
        var due = SecretDueRules.Evaluate(
            [V(1, createdAt: Now.AddDays(-80), notBefore: Now.AddDays(-30))],
            90,
            Now
        );

        Assert.Equal(Now.AddDays(60), due!.DueAt);
    }

    [Theory]
    [InlineData(5, 10, SecretDueReasons.Expiry)]
    [InlineData(10, 5, SecretDueReasons.Rotation)]
    [InlineData(7, 7, SecretDueReasons.Expiry)]
    public void EarlierOfExpiryAndRotationWins(int expiresIn, int rotationIn, string reason)
    {
        var due = SecretDueRules.Evaluate(
            [V(1, createdAt: Now, expiresAt: Now.AddDays(expiresIn))],
            rotationIn,
            Now
        );

        Assert.Equal(reason, due!.Reason);
        Assert.Equal(Now.AddDays(Math.Min(expiresIn, rotationIn)), due.DueAt);
    }

    [Fact]
    public void OnlyTheCurrentVersionCounts()
    {
        // v1 expires soon but v2 is served and has no expiry.
        var due = SecretDueRules.Evaluate(
            [V(1, expiresAt: Now.AddDays(2)), V(2, createdAt: Now)],
            30,
            Now
        );

        Assert.Equal(new SecretDue(SecretDueReasons.Rotation, Now.AddDays(30), 2), due);
    }

    [Fact]
    public void ScheduledReplacementBeforeTheDueDate_MeansNothingIsDue()
    {
        var due = SecretDueRules.Evaluate(
            [V(1, expiresAt: Now.AddDays(5)), V(2, notBefore: Now.AddDays(4))],
            null,
            Now
        );

        Assert.Null(due);
    }

    [Fact]
    public void ScheduledReplacementAfterTheDueDate_DoesNotCount()
    {
        var due = SecretDueRules.Evaluate(
            [V(1, expiresAt: Now.AddDays(5)), V(2, notBefore: Now.AddDays(6))],
            null,
            Now
        );

        Assert.Equal(SecretDueReasons.Expiry, due!.Reason);
    }

    [Fact]
    public void LatestEnabledVersionExpired_IsExpired()
    {
        var due = SecretDueRules.Evaluate(
            [V(1, expiresAt: Now.AddDays(-3)), V(2, enabled: false)],
            30,
            Now
        );

        Assert.Equal(new SecretDue(SecretDueReasons.Expired, Now.AddDays(-3), 1), due);
    }

    [Fact]
    public void NoValidVersionWithoutExpiry_IsNotDue() =>
        Assert.Null(SecretDueRules.Evaluate([V(1, enabled: false)], 30, Now));

    [Theory]
    [InlineData(40, null)]
    [InlineData(30, 30)]
    [InlineData(20, 30)]
    [InlineData(7, 7)]
    [InlineData(2, 7)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void CrossedThreshold_IsTheTightestOneReached(int dueInDays, int? expected) =>
        Assert.Equal(
            expected,
            SecretDueRules.CrossedThreshold(Now.AddDays(dueInDays), Now, [30, 7, 1])
        );

    [Fact]
    public void DaysRemaining_CountsUtcCalendarDays()
    {
        Assert.Equal(1, SecretDueRules.DaysRemaining(Now.Date.AddDays(1).AddHours(1), Now));
        Assert.Equal(0, SecretDueRules.DaysRemaining(Now.AddHours(1), Now));
        Assert.Equal(-2, SecretDueRules.DaysRemaining(Now.AddDays(-2), Now));
    }
}
