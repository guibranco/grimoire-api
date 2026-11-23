using System.Text.Json;
using Grimoire.Core.FeatureFlags;

namespace Grimoire.Tests;

/// <summary>The reference evaluator against the shared test vectors and edge cases.</summary>
public class FeatureFlagEvaluatorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<JsonDocument> Vectors = new(() =>
        JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "FeatureFlags", "test-vectors.json")
            )
        )
    );

    private static JsonElement Root => Vectors.Value.RootElement;

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Root.GetProperty("cases").GetArrayLength(); i++)
            data.Add(i);
        return data;
    }

    private static (FlagDefinition Flag, FlagEnvironmentState State) LoadFlag(string name)
    {
        var f = Root.GetProperty("flags").GetProperty(name);
        var variants = f.GetProperty("variants")
            .EnumerateArray()
            .Select(v => new FlagVariant(
                v.GetProperty("key").GetString()!,
                v.GetProperty("value").GetRawText()
            ))
            .ToList();
        var flag = new FlagDefinition(
            f.GetProperty("key").GetString()!,
            f.GetProperty("salt").GetString()!,
            variants,
            f.GetProperty("defaultVariant").GetString()!
        );
        var s = f.GetProperty("state");
        var state = new FlagEnvironmentState(
            s.GetProperty("enabled").GetBoolean(),
            s.GetProperty("offVariant").GetString(),
            s.GetProperty("rules").Deserialize<List<TargetingRule>>(Web)!,
            s.GetProperty("fallthrough").Deserialize<FlagServe?>(Web),
            1
        );
        return (flag, state);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void SharedVector(int index)
    {
        var c = Root.GetProperty("cases")[index];
        var (flag, state) = LoadFlag(c.GetProperty("flag").GetString()!);
        var context = c.GetProperty("context")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var expected = c.GetProperty("expected");

        var result = FlagEvaluator.Evaluate(flag, state, context);

        Assert.Equal(
            (
                expected.GetProperty("variant").GetString(),
                expected.GetProperty("reason").GetString(),
                Nullable(expected.GetProperty("matchedRuleIndex")),
                Nullable(expected.GetProperty("bucket"))
            ),
            (result.Variant, result.Reason, result.MatchedRuleIndex, result.Bucket)
        );
    }

    private static int? Nullable(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    [Fact]
    public void SharedVectors_HashesAndBuckets()
    {
        foreach (var h in Root.GetProperty("hashes").EnumerateArray())
            Assert.Equal(
                h.GetProperty("murmur3_32").GetUInt32(),
                Murmur3.Hash32(h.GetProperty("input").GetString()!)
            );
        foreach (var b in Root.GetProperty("buckets").EnumerateArray())
            Assert.Equal(
                b.GetProperty("bucket").GetInt32(),
                FlagEvaluator.Bucket(
                    b.GetProperty("flagKey").GetString()!,
                    b.GetProperty("salt").GetString()!,
                    b.GetProperty("value").GetString()!
                )
            );
    }

    [Theory]
    [InlineData("", 0u, 0u)]
    [InlineData("", 1u, 0x514E28B7u)]
    [InlineData("", 0xffffffffu, 0x81F16F39u)]
    [InlineData("aaaa", 0x9747b28cu, 0x5A97808Au)]
    [InlineData("Hello, world!", 0x9747b28cu, 0x24884CBAu)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x9747b28cu, 0x2FA826CDu)]
    public void Murmur3_MatchesPublishedValues(string input, uint seed, uint expected) =>
        Assert.Equal(expected, Murmur3.Hash32(input, seed));

    [Fact]
    public void Murmur3_HandlesEveryTailLength()
    {
        Assert.Equal(0x2362F9DEu, Murmur3.Hash32(new byte[4]));
        Assert.NotEqual(Murmur3.Hash32("abc"), Murmur3.Hash32("abd"));
        Assert.NotEqual(Murmur3.Hash32("ab"), Murmur3.Hash32("a"));
    }

    [Fact]
    public void Rollout_IsStickyWhenRaised()
    {
        var flag = new FlagDefinition("f", "salt", FlagTypes.BooleanVariants, FlagTypes.Off);
        FlagEnvironmentState At(decimal on) =>
            new(true, null, [], new FlagServe(Rollout: [new("on", on), new("off", 100 - on)]), 1);

        var users = Enumerable.Range(0, 500).Select(i => $"user-{i}").ToList();
        var at10 = users.Where(u => Serve(flag, At(10), u) == "on").ToHashSet();
        var at20 = users.Where(u => Serve(flag, At(20), u) == "on").ToHashSet();

        Assert.True(at10.IsSubsetOf(at20));
        Assert.InRange(at10.Count, 25, 75);
        Assert.InRange(at20.Count, 70, 130);
    }

    private static string Serve(FlagDefinition flag, FlagEnvironmentState state, string user) =>
        FlagEvaluator
            .Evaluate(flag, state, new Dictionary<string, string> { ["userId"] = user })
            .Variant;

    [Fact]
    public void PickVariant_GivesRoundingLeftoversToTheLastSlice()
    {
        RolloutSlice[] rollout = [new("a", 33.33m), new("b", 33.33m), new("c", 33.33m)];
        Assert.Equal("a", FlagEvaluator.PickVariant(rollout, 3332));
        Assert.Equal("b", FlagEvaluator.PickVariant(rollout, 3333));
        Assert.Equal("c", FlagEvaluator.PickVariant(rollout, 9999));
    }

    [Fact]
    public void NoState_IsOff_ServingTheDefaultVariant()
    {
        var flag = new FlagDefinition("f", "s", FlagTypes.BooleanVariants, FlagTypes.On);
        var result = FlagEvaluator.Evaluate(flag, null, new Dictionary<string, string>());
        Assert.Equal((FlagTypes.On, EvaluationReasons.Off), (result.Variant, result.Reason));
    }

    [Theory]
    [InlineData(ConditionOperators.In)]
    [InlineData(ConditionOperators.NotIn)]
    [InlineData(ConditionOperators.Matches)]
    [InlineData(ConditionOperators.SemVerGte)]
    public void MissingAttribute_NeverMatches(string op) =>
        Assert.False(
            FlagEvaluator.Matches(
                new FlagCondition("absent", op, ["1.0.0"]),
                new Dictionary<string, string>()
            )
        );

    [Theory]
    [InlineData("unknownOperator", "x")]
    [InlineData(ConditionOperators.Matches, "(")]
    [InlineData(ConditionOperators.SemVerGte, "1.2")]
    [InlineData(ConditionOperators.SemVerLt, "1.2")]
    public void InvalidOperatorOrValue_NeverMatches(string op, string value) =>
        Assert.False(
            FlagEvaluator.Matches(
                new FlagCondition("v", op, [value]),
                new Dictionary<string, string> { ["v"] = "1.2.3" }
            )
        );

    [Fact]
    public void Regex_TimesOutSafely()
    {
        var evil = new FlagCondition("v", ConditionOperators.Matches, ["^(a+)+$"]);
        var input = new string('a', 5000) + "!";
        Assert.False(FlagEvaluator.Matches(evil, new Dictionary<string, string> { ["v"] = input }));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.10", "1.0.9", 1)]
    [InlineData("v1.2.3+build", "1.2.3", 0)]
    public void SemVer_FollowsPrecedence(string a, string b, int expected)
    {
        Assert.True(SemVer.TryParse(a, out var va));
        Assert.True(SemVer.TryParse(b, out var vb));
        Assert.Equal(expected, va.CompareTo(vb));
        Assert.Equal(-expected, vb.CompareTo(va));
        Assert.Equal(1, va.CompareTo(null));
        Assert.Equal(expected < 0, va < vb);
        Assert.Equal(expected <= 0, va <= vb);
        Assert.Equal(expected > 0, va > vb);
        Assert.Equal(expected >= 0, va >= vb);
        Assert.True((SemVer?)null < va);
        Assert.True((SemVer?)null <= null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("99999999999.0.0")]
    public void SemVer_RejectsInvalidVersions(string? text) =>
        Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void Helpers()
    {
        Assert.True(FlagEvaluator.IsValidRegex("^a$"));
        Assert.False(FlagEvaluator.IsValidRegex("("));
        Assert.False(FlagEvaluator.IsValidRegex(null));
        Assert.Equal("true", FlagEvaluator.ToContextString(true));
        Assert.Equal("false", FlagEvaluator.ToContextString(false));
        Assert.Equal("1.5", FlagEvaluator.ToContextString(1.5));
        Assert.Equal(string.Empty, FlagEvaluator.ToContextString(null));
        Assert.Equal("x", FlagEvaluator.ToContextString(new Uri("x", UriKind.Relative)));
        Assert.True(FlagTypes.IsKnown(FlagTypes.Multivariate));
        Assert.False(FlagTypes.IsKnown("ternary"));
        Assert.True(ConditionOperators.IsSingleValue(ConditionOperators.SemVerLt));
        Assert.Equal("userId", new FlagServe(Variant: "on").EffectiveBucketBy);
        Assert.Equal("tenant", new FlagServe(BucketBy: "tenant").EffectiveBucketBy);
    }
}
