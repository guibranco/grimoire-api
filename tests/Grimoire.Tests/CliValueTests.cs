using Grimoire.Cli;
using Grimoire.Cli.Commands;

namespace Grimoire.Tests;

public class CliValueTests
{
    private static readonly Dictionary<string, string> Types = new()
    {
        ["AllowedIps"] = "array",
        ["Smtp"] = "json",
        ["Broken"] = "json",
    };

    private static readonly Dictionary<string, string> Configs = new()
    {
        ["AllowedIps"] = """["10.0.0.1","10.0.0.2"]""",
        ["Smtp"] = """{"Host":"mail","Port":25,"Tls":true,"Relay":null,"Tags":[],"Opts":{}}""",
        ["Broken"] = "{not json",
        ["Plain"] = "[looks like json but is a string]",
    };

    [Fact]
    public void Expand_Raw_KeepsEveryValueAsIs()
    {
        var warnings = new List<string>();

        var result = StructuredValues.Expand(Configs, Types, StructuredMode.Raw, warnings.Add);

        Assert.Equal(Configs, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Expand_Flatten_FollowsTheConsumerRules()
    {
        var warnings = new List<string>();

        var result = StructuredValues.Expand(Configs, Types, StructuredMode.Flatten, warnings.Add);

        Assert.Equal("10.0.0.1", result["AllowedIps:0"]);
        Assert.Equal("10.0.0.2", result["AllowedIps:1"]);
        Assert.False(result.ContainsKey("AllowedIps"));
        Assert.Equal("mail", result["Smtp:Host"]);
        Assert.Equal("25", result["Smtp:Port"]);
        Assert.Equal("true", result["Smtp:Tls"]);
        Assert.Equal("", result["Smtp:Relay"]);
        Assert.DoesNotContain(result.Keys, k =>
            k.StartsWith("Smtp:Tags") || k.StartsWith("Smtp:Opts")
        );
        Assert.Equal("{not json", result["Broken"]);
        Assert.Equal("[looks like json but is a string]", result["Plain"]);
        Assert.Single(warnings);
        Assert.Contains("Broken", warnings[0]);
    }

    [Fact]
    public void Expand_Both_KeepsTheRawValueBesideTheFlattenedOnes()
    {
        var result = StructuredValues.Expand(Configs, Types, StructuredMode.Both, _ => { });

        Assert.Equal(Configs["AllowedIps"], result["AllowedIps"]);
        Assert.Equal("10.0.0.1", result["AllowedIps:0"]);
    }

    [Fact]
    public void ToJsonObject_EmitsStructuredValuesAsRealJson()
    {
        var json = StructuredValues.ToJsonObject(Configs, Types, _ => { });

        Assert.Equal(2, json["AllowedIps"]!.AsArray().Count);
        Assert.Equal(25, json["Smtp"]!["Port"]!.GetValue<int>());
        Assert.Equal("{not json", json["Broken"]!.GetValue<string>());
        Assert.Equal("[looks like json but is a string]", json["Plain"]!.GetValue<string>());
        Assert.Equal(["AllowedIps", "Broken", "Plain", "Smtp"], json.Select(p => p.Key));
    }

    [Theory]
    [InlineData("flatten", "Flatten")]
    [InlineData("BOTH", "Both")]
    [InlineData("raw", "Raw")]
    [InlineData(null, "Raw")]
    public void ParseMode_DefaultsToRaw(string? value, string expected) =>
        Assert.Equal(expected, StructuredValues.ParseMode(value).ToString());

    [Fact]
    public void Diff_ComparesConfigurationByValue_AndSecretsByPresenceOnly()
    {
        var items = EnvironmentDiff.Compare(
            new Dictionary<string, string>
            {
                ["A"] = "1",
                ["B"] = "same",
                ["C"] = "old\nline",
            },
            new Dictionary<string, string>
            {
                ["B"] = "same",
                ["C"] = "new",
                ["D"] = "4",
            },
            new HashSet<string> { "only-first", "both" },
            new HashSet<string> { "both", "only-second" }
        );

        Assert.Equal(
            [
                "- config A=1",
                "~ config C: old\\nline → new",
                "+ config D=4",
                "- secret only-first",
                "+ secret only-second",
            ],
            items.Select(EnvironmentDiff.Format)
        );
    }

    [Fact]
    public void Diff_IsEmptyForIdenticalEnvironments()
    {
        var values = new Dictionary<string, string> { ["A"] = "1" };
        var secrets = new HashSet<string> { "s" };

        Assert.Empty(EnvironmentDiff.Compare(values, values, secrets, secrets));
    }

    [Theory]
    [InlineData(".env", "env")]
    [InlineData("config/.env.production", "env")]
    [InlineData("prod.env", "env")]
    [InlineData("appsettings.Development.json", "appsettings")]
    [InlineData("values.JSON", "json")]
    [InlineData("config.php", "php")]
    [InlineData("settings.yaml", null)]
    public void DetectFormat_UsesTheFileName(string path, string? expected) =>
        Assert.Equal(expected, WriteCommands.DetectFormat(path));

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("secrets", true, false)]
    [InlineData(" Configurations ", false, true)]
    [InlineData("secrets,configurations", true, true)]
    public void TryParseInclude_AcceptsKnownNames(
        string? include,
        bool secrets,
        bool configurations
    )
    {
        Assert.True(CliApp.TryParseInclude(include, out var s, out var c));
        Assert.Equal(secrets, s);
        Assert.Equal(configurations, c);
    }

    [Theory]
    [InlineData("flags")]
    [InlineData(",")]
    public void TryParseInclude_RejectsUnknownOrEmptyLists(string include) =>
        Assert.False(CliApp.TryParseInclude(include, out _, out _));
}
