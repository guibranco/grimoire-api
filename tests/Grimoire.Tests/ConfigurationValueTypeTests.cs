using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Validators.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Services;

namespace Grimoire.Tests;

public class ConfigurationValueTypeTests
{
    [Theory]
    [InlineData("string", ConfigurationValueType.String)]
    [InlineData("Multiline", ConfigurationValueType.Multiline)]
    [InlineData("ARRAY", ConfigurationValueType.Array)]
    [InlineData("json", ConfigurationValueType.Json)]
    public void TryParseName_AcceptsKnownNamesCaseInsensitively(
        string name,
        ConfigurationValueType expected
    )
    {
        Assert.True(ConfigurationValueNormalizer.TryParseName(name, out var type));
        Assert.Equal(expected, type);
        Assert.Equal(name.ToLowerInvariant(), ConfigurationValueNormalizer.ToName(type));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseName_RejectsUnknownNames(string? name) =>
        Assert.False(ConfigurationValueNormalizer.TryParseName(name, out _));

    [Fact]
    public void String_IsStoredAsGiven()
    {
        Assert.True(Normalize(ConfigurationValueType.String, "  a,b \r\n", out var stored));
        Assert.Equal("  a,b \r\n", stored);
    }

    [Fact]
    public void Multiline_NormalisesLineEndings()
    {
        Assert.True(Normalize(ConfigurationValueType.Multiline, "a\r\nb\rc\nd", out var stored));
        Assert.Equal("a\nb\nc\nd", stored);
    }

    [Fact]
    public void Array_AcceptsJsonArrayOfStrings_AndMinifiesIt()
    {
        Assert.True(
            Normalize(
                ConfigurationValueType.Array,
                "[ \"10.0.0.1\",\n \"10.0.0.2\" ]",
                out var stored
            )
        );
        Assert.Equal("[\"10.0.0.1\",\"10.0.0.2\"]", stored);
    }

    [Fact]
    public void Array_AcceptsOneItemPerLine()
    {
        Assert.True(
            Normalize(
                ConfigurationValueType.Array,
                " 10.0.0.1 \r\n\r\n192.168.1.0/24\n",
                out var stored
            )
        );
        Assert.Equal("[\"10.0.0.1\",\"192.168.1.0/24\"]", stored);
    }

    [Fact]
    public void Array_EmptyValue_IsAnEmptyArray()
    {
        Assert.True(Normalize(ConfigurationValueType.Array, "", out var stored));
        Assert.Equal("[]", stored);
    }

    [Fact]
    public void Array_KeepsNonAsciiAndPlusReadable()
    {
        Assert.True(Normalize(ConfigurationValueType.Array, "café\na+b", out var stored));
        Assert.Equal("[\"café\",\"a+b\"]", stored);
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("[\"a\", null]")]
    [InlineData("[\"a\", {\"b\": 1}]")]
    [InlineData("[\"unterminated\"")]
    public void Array_RejectsNonStringItemsAndInvalidJson(string value) =>
        Assert.False(Normalize(ConfigurationValueType.Array, value, out _));

    [Fact]
    public void Json_AcceptsObject_AndMinifiesIt()
    {
        Assert.True(
            Normalize(
                ConfigurationValueType.Json,
                "{\n  \"retries\": 3,\n  \"hosts\": [\"a\", \"b\"]\n}",
                out var stored
            )
        );
        Assert.Equal("{\"retries\":3,\"hosts\":[\"a\",\"b\"]}", stored);
    }

    [Fact]
    public void Json_AcceptsArray() =>
        Assert.True(Normalize(ConfigurationValueType.Json, "[1, {\"a\": true}]", out _));

    [Theory]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("{ not json }")]
    [InlineData("")]
    public void Json_RejectsScalarsAndInvalidJson(string value) =>
        Assert.False(Normalize(ConfigurationValueType.Json, value, out _));

    [Fact]
    public void CreateValidator_DefaultsToString_SoAnyValueIsValid()
    {
        var result = new CreateConfigurationRequestValidator().Validate(
            new CreateConfigurationRequest("local", "Key", "{ not json")
        );
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateValidator_RejectsInvalidJsonForJsonType()
    {
        var result = new CreateConfigurationRequestValidator().Validate(
            new CreateConfigurationRequest("local", "Key", "{ not json", ValueType: "json")
        );
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public void CreateValidator_RejectsUnknownValueType()
    {
        var result = new CreateConfigurationRequestValidator().Validate(
            new CreateConfigurationRequest("local", "Key", "x", ValueType: "list")
        );
        Assert.Contains(result.Errors, e => e.PropertyName == "ValueType");
    }

    [Fact]
    public void UpdateValidator_WithoutValueType_LeavesValueCheckToController()
    {
        var result = new UpdateConfigurationRequestValidator().Validate(
            new UpdateConfigurationRequest("{ not json")
        );
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GlobalCreateValidator_RejectsNonStringArrayItems()
    {
        var result = new CreateGlobalConfigurationRequestValidator().Validate(
            new CreateGlobalConfigurationRequest("Key", "[1,2]", ValueType: "array")
        );
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public void Merge_CarriesValueType_AndApplicationValueReplacesGlobalWhole()
    {
        var app = new ConfigurationEntry
        {
            Key = "AllowedIps",
            Value = "[\"10.0.0.1\"]",
            ValueType = ConfigurationValueType.Array,
        };
        var global = new GlobalConfigurationEntry
        {
            Key = "AllowedIps",
            Value = "[\"1.1.1.1\",\"2.2.2.2\"]",
            ValueType = ConfigurationValueType.Array,
        };
        var globalOnly = new GlobalConfigurationEntry
        {
            Key = "Smtp",
            Value = "{\"host\":\"x\"}",
            ValueType = ConfigurationValueType.Json,
        };

        var merged = ConfigurationPrecedence.Merge([app], [global, globalOnly], "local", true);

        var ips = Assert.Single(merged, e => e.Key == "AllowedIps");
        Assert.Equal("[\"10.0.0.1\"]", ips.Value);
        Assert.Equal(ConfigurationValueType.Array, ips.ValueType);
        Assert.Equal(
            ConfigurationValueType.Json,
            Assert.Single(merged, e => e.Key == "Smtp").ValueType
        );
    }

    private static bool Normalize(ConfigurationValueType type, string value, out string? stored)
    {
        var ok = ConfigurationValueNormalizer.TryNormalize(type, value, out stored, out var error);
        Assert.Equal(ok, error is null);
        return ok;
    }
}
