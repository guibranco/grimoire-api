using Grimoire.Core.Export;
using Grimoire.Core.Import;

namespace Grimoire.Tests;

public class ExportFormatterTests
{
    [Theory]
    [InlineData("Feature:DarkMode", "FEATURE__DARKMODE")]
    [InlineData("db-host", "DB_HOST")]
    [InlineData("ConnectionStrings:Main.Db", "CONNECTIONSTRINGS__MAIN_DB")]
    [InlineData("already_UPPER_9", "ALREADY_UPPER_9")]
    [InlineData("9lives", "_9LIVES")]
    [InlineData("", "_")]
    public void EnvKey_NormalisesNames(string key, string expected) =>
        Assert.Equal(expected, ExportFormatter.EnvKey(key));

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("has $HOME and \"quotes\"", "'has $HOME and \"quotes\"'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("two\nlines", "\"two\\nlines\"")]
    [InlineData("it's $5 \\ \"x\"\r", "\"it's \\$5 \\\\ \\\"x\\\"\\r\"")]
    public void EnvValue_QuotesForDotenv(string value, string expected) =>
        Assert.Equal(expected, ExportFormatter.EnvValue(value));

    [Fact]
    public void ToEnv_SortsLines_SecretWinsOnTheSameName_AndRoundTripsThroughTheImporter()
    {
        var values = new ExportedValues(
            new Dictionary<string, string> { ["api-key"] = "from secret", ["note"] = "it's\nfine" },
            new Dictionary<string, string>
            {
                ["Api:Key"] = "unused",
                ["API-KEY"] = "from config",
                ["Feature:DarkMode"] = "true",
            }
        );

        var env = ExportFormatter.ToEnv(values);

        Assert.Equal(
            "API_KEY='from secret'\nAPI__KEY='unused'\nFEATURE__DARKMODE='true'\nNOTE=\"it's\\nfine\"\n",
            env
        );
        var parsed = ImportParser
            .Parse(ImportFormat.Env, env)
            .Values.ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("from secret", parsed["API_KEY"]);
        Assert.Equal("it's\nfine", parsed["NOTE"]);
    }

    [Fact]
    public void ToEnv_IsEmptyWithoutValues() =>
        Assert.Equal(
            "",
            ExportFormatter.ToEnv(
                new ExportedValues(
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()
                )
            )
        );

    [Fact]
    public void ToDotnet_KeepsKeys_SecretWins()
    {
        var flat = ExportFormatter.ToDotnet(
            new ExportedValues(
                new Dictionary<string, string> { ["Smtp:Host"] = "secret" },
                new Dictionary<string, string> { ["Smtp:Host"] = "config", ["A:B"] = "1" }
            )
        );

        Assert.Equal(["A:B", "Smtp:Host"], flat.Keys);
        Assert.Equal("secret", flat["Smtp:Host"]);
    }

    [Theory]
    [InlineData(null, ExportFormat.Json)]
    [InlineData("", ExportFormat.Json)]
    [InlineData("ENV", ExportFormat.Env)]
    [InlineData("dotnet", ExportFormat.Dotnet)]
    public void TryParseFormat_AcceptsKnownNames(string? value, ExportFormat expected)
    {
        Assert.True(ExportFormatter.TryParseFormat(value, out var format));
        Assert.Equal(expected, format);
    }

    [Fact]
    public void TryParseFormat_RejectsUnknownNames() =>
        Assert.False(ExportFormatter.TryParseFormat("yaml", out _));
}
