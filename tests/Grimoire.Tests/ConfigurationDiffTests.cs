using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Services;
using Xunit;

namespace Grimoire.Tests;

public class ConfigurationDiffTests
{
    private static EffectiveConfigurationEntry E(
        string key,
        string value,
        ConfigurationValueType type = ConfigurationValueType.String,
        ValueSource source = ValueSource.Application
    ) => new(key, value, null, source, null, false, type);

    [Fact]
    public void ClassifiesKeys_AndSortsThem()
    {
        var diff = ConfigurationDiff.Compare(
            [E("Same", "1"), E("Changed", "a"), E("OnlyFrom", "x")],
            [E("Same", "1"), E("Changed", "b"), E("OnlyTo", "y")]
        );

        Assert.Equal(
            [
                ("Changed", ConfigurationDiffStatus.Changed),
                ("OnlyFrom", ConfigurationDiffStatus.Removed),
                ("OnlyTo", ConfigurationDiffStatus.Added),
            ],
            diff.Select(d => (d.Key, d.Status))
        );
        Assert.Null(diff.Single(d => d.Key == "OnlyTo").From);
        Assert.Null(diff.Single(d => d.Key == "OnlyFrom").To);
    }

    [Fact]
    public void ValueTypeChange_IsAChange() =>
        Assert.Equal(
            ConfigurationDiffStatus.Changed,
            ConfigurationDiff
                .Compare([E("K", "a")], [E("K", "a", ConfigurationValueType.Multiline)])
                .Single()
                .Status
        );

    [Fact]
    public void SourceAlone_IsNotAChange()
    {
        var diff = ConfigurationDiff.Compare(
            [E("K", "a", source: ValueSource.Global)],
            [E("K", "a")],
            includeUnchanged: true
        );

        Assert.Equal(ConfigurationDiffStatus.Unchanged, Assert.Single(diff).Status);
    }

    [Fact]
    public void KeysAreCaseSensitive() =>
        Assert.Equal(2, ConfigurationDiff.Compare([E("key", "a")], [E("Key", "a")]).Count);
}
