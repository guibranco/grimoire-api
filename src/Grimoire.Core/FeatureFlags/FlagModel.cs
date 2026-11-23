using System.Text.Json.Serialization;

namespace Grimoire.Core.FeatureFlags;

/// <summary>Wire names of flag types.</summary>
public static class FlagTypes
{
    /// <summary>Two implicit variants, <c>on</c> (<c>true</c>) and <c>off</c> (<c>false</c>).</summary>
    public const string Boolean = "boolean";

    /// <summary>Two or more named variants with JSON values (A/B/n).</summary>
    public const string Multivariate = "multivariate";

    public const string On = "on";
    public const string Off = "off";

    public static bool IsKnown(string? type) => type is Boolean or Multivariate;

    /// <summary>The fixed variants of a boolean flag.</summary>
    public static IReadOnlyList<FlagVariant> BooleanVariants { get; } =
    [new(On, "true"), new(Off, "false")];
}

/// <summary>Wire names of targeting condition operators. Every operator compares strings ordinally.</summary>
public static class ConditionOperators
{
    public const string In = "in";
    public const string NotIn = "notIn";
    public const string EqualTo = "equals";
    public const string Contains = "contains";
    public const string StartsWith = "startsWith";
    public const string EndsWith = "endsWith";
    public const string Matches = "matches";
    public const string SemVerGte = "semVerGte";
    public const string SemVerLt = "semVerLt";

    public static IReadOnlyList<string> All { get; } =
    [In, NotIn, EqualTo, Contains, StartsWith, EndsWith, Matches, SemVerGte, SemVerLt];

    public static bool IsKnown(string? op) => op is not null && All.Contains(op);

    /// <summary>Operators that take exactly one value.</summary>
    public static bool IsSingleValue(string? op) => op is SemVerGte or SemVerLt;
}

/// <param name="Value">The variant's value as raw JSON (e.g. <c>true</c>, <c>"blue"</c>, <c>{"limit":5}</c>).</param>
public sealed record FlagVariant(string Key, string Value);

/// <param name="Weight">Percentage of the bucket range, 0–100 with up to two decimals.</param>
public sealed record RolloutSlice(string Variant, decimal Weight);

/// <summary>
/// What to serve: a fixed <see cref="Variant"/>, or a weighted <see cref="Rollout"/> bucketed by
/// the <see cref="BucketBy"/> context attribute (default <c>userId</c>). Exactly one of the two.
/// </summary>
public sealed record FlagServe(
    string? Variant = null,
    IReadOnlyList<RolloutSlice>? Rollout = null,
    string? BucketBy = null
)
{
    public const string DefaultBucketBy = "userId";

    [JsonIgnore]
    public string EffectiveBucketBy =>
        string.IsNullOrWhiteSpace(BucketBy) ? DefaultBucketBy : BucketBy;
}

/// <param name="Attribute">Context attribute, e.g. <c>userId</c>, <c>email</c>, <c>country</c>.</param>
/// <param name="Operator">One of <see cref="ConditionOperators"/>.</param>
public sealed record FlagCondition(string Attribute, string Operator, IReadOnlyList<string> Values);

/// <summary>All <see cref="Conditions"/> must match; then <see cref="Serve"/> applies.</summary>
public sealed record TargetingRule(IReadOnlyList<FlagCondition> Conditions, FlagServe Serve);

/// <summary>A flag as the evaluator needs it.</summary>
public sealed record FlagDefinition(
    string Key,
    string Salt,
    IReadOnlyList<FlagVariant> Variants,
    string DefaultVariant
);

/// <summary>
/// A flag's state in one environment. A flag never configured in an environment is
/// <see cref="Default"/>: disabled, serving the default variant.
/// </summary>
public sealed record FlagEnvironmentState(
    bool Enabled,
    string? OffVariant,
    IReadOnlyList<TargetingRule> Rules,
    FlagServe? Fallthrough,
    int Version
)
{
    public static FlagEnvironmentState Default { get; } = new(false, null, [], null, 0);
}
