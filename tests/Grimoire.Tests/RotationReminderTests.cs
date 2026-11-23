using Grimoire.Api.Rotation;
using Grimoire.Core.Dashboard;
using Grimoire.Core.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Grimoire.Tests;

public class RotationReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static RotationReminderOptions Bind(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(RotationReminderOptions.SectionName)
            .Get<RotationReminderOptions>()
        ?? new RotationReminderOptions();

    [Fact]
    public void Thresholds_DefaultTo30_7_1() =>
        Assert.Equal([30, 7, 1], new RotationReminderOptions().EffectiveThresholds);

    [Fact]
    public void ConfiguredThresholds_ReplaceTheDefaults()
    {
        var options = Bind(
            new()
            {
                ["RotationReminders:Thresholds:0"] = "3",
                ["RotationReminders:Thresholds:1"] = "14",
                ["RotationReminders:Thresholds:2"] = "0",
                ["RotationReminders:Thresholds:3"] = "14",
            }
        );

        Assert.Equal([14, 3], options.EffectiveThresholds);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not a url", false)]
    [InlineData("ftp://example.test/hook", false)]
    [InlineData("https://example.test/hook", true)]
    public void HasWebhook_RequiresAnAbsoluteHttpUrl(string? url, bool expected) =>
        Assert.Equal(expected, new RotationReminderOptions { WebhookUrl = url }.HasWebhook);

    private static ExpiringSecret Item(string? app, string reason, DateTimeOffset dueAt) =>
        new(Guid.NewGuid(), app, app, "DB_PASSWORD", "production", 3, reason, dueAt, 90);

    [Fact]
    public void Summary_DescribesEachSecretWithoutValues()
    {
        var text = RotationReminderNotifier.Summary(
            [
                Item("billing", SecretDueReasons.Rotation, Now.AddDays(7)),
                Item(null, SecretDueReasons.Expiry, Now.AddDays(1)),
                Item("billing", SecretDueReasons.Expired, Now.AddDays(-2)),
                Item("billing", SecretDueReasons.Rotation, Now.AddHours(2)),
            ],
            Now
        );

        Assert.Equal(
            """
            Grimoire: 4 secrets need rotation
            - billing/DB_PASSWORD (production): rotation due in 7 days (2026-11-30)
            - global/DB_PASSWORD (production): expires in 1 day (2026-11-24)
            - billing/DB_PASSWORD (production): expired 2 days ago (2026-11-21)
            - billing/DB_PASSWORD (production): rotation due today (2026-11-23)
            """.ReplaceLineEndings("\n"),
            text
        );
    }

    [Fact]
    public void Key_ChangesWithTheDueDateAndThreshold()
    {
        var item = Item("billing", SecretDueReasons.Rotation, Now.AddDays(7));

        Assert.NotEqual(
            RotationReminderNotifier.KeyOf(item, 7),
            RotationReminderNotifier.KeyOf(item, 1)
        );
        Assert.NotEqual(
            RotationReminderNotifier.KeyOf(item, 7),
            RotationReminderNotifier.KeyOf(item with { DueAt = Now.AddDays(8) }, 7)
        );
        Assert.StartsWith("a:", RotationReminderNotifier.KeyOf(item, 7));
        Assert.StartsWith(
            "g:",
            RotationReminderNotifier.KeyOf(item with { Application = null }, 7)
        );
    }
}
