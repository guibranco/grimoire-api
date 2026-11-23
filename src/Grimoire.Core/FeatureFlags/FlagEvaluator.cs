using System.Globalization;
using System.Text.RegularExpressions;

namespace Grimoire.Core.FeatureFlags;

/// <summary>Why a variant was served.</summary>
public static class EvaluationReasons
{
    /// <summary>The flag is disabled in the environment: the off variant.</summary>
    public const string Off = "OFF";

    /// <summary>A targeting rule matched (see the matched rule index).</summary>
    public const string RuleMatch = "RULE_MATCH";

    /// <summary>No rule matched: the fallthrough (default rule).</summary>
    public const string Fallthrough = "FALLTHROUGH";

    /// <summary>A rollout needed the bucket-by attribute and the context lacks it: the default variant.</summary>
    public const string MissingBucketBy = "MISSING_BUCKET_BY";
}

/// <param name="MatchedRuleIndex">Zero-based index of the rule that matched, for <c>RULE_MATCH</c>.</param>
/// <param name="Bucket">0–9999 when a rollout bucketed the context.</param>
public sealed record FlagEvaluation(
    string Variant,
    string Reason,
    int? MatchedRuleIndex = null,
    int? Bucket = null
);

/// <summary>
/// The reference implementation of the evaluation spec (<c>docs/feature-flags/evaluation-spec.md</c>).
/// Every client must produce the same variant for the same flag, state and context.
/// </summary>
public static class FlagEvaluator
{
    /// <summary>Buckets per flag: 0.01 % resolution.</summary>
    public const int BucketCount = 10_000;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static FlagEvaluation Evaluate(
        FlagDefinition flag,
        FlagEnvironmentState? state,
        IReadOnlyDictionary<string, string> context
    )
    {
        state ??= FlagEnvironmentState.Default;

        // 1. Disabled → off variant (or the default variant when none is set).
        if (!state.Enabled)
            return new FlagEvaluation(
                state.OffVariant ?? flag.DefaultVariant,
                EvaluationReasons.Off
            );

        // 2. First rule whose conditions all match wins.
        for (var i = 0; i < state.Rules.Count; i++)
        {
            var rule = state.Rules[i];
            if (rule.Conditions.All(c => Matches(c, context)))
                return Serve(flag, rule.Serve, context, EvaluationReasons.RuleMatch, i);
        }

        // 3. Fallthrough (none configured → the default variant).
        return state.Fallthrough is null
            ? new FlagEvaluation(flag.DefaultVariant, EvaluationReasons.Fallthrough)
            : Serve(flag, state.Fallthrough, context, EvaluationReasons.Fallthrough, null);
    }

    /// <summary><c>murmur3_32("{flagKey}.{salt}.{value}") % 10000</c>.</summary>
    public static int Bucket(string flagKey, string salt, string bucketByValue) =>
        (int)(Murmur3.Hash32($"{flagKey}.{salt}.{bucketByValue}") % BucketCount);

    /// <summary>
    /// The variant whose cumulative weight range contains <paramref name="bucket"/>: slices take
    /// <c>weight × 100</c> buckets each, in order. Rounding leftovers go to the last slice.
    /// </summary>
    public static string PickVariant(IReadOnlyList<RolloutSlice> rollout, int bucket)
    {
        var upper = 0;
        foreach (var slice in rollout)
        {
            upper += (int)Math.Round(slice.Weight * 100m, MidpointRounding.AwayFromZero);
            if (bucket < upper)
                return slice.Variant;
        }
        return rollout[^1].Variant;
    }

    private static FlagEvaluation Serve(
        FlagDefinition flag,
        FlagServe serve,
        IReadOnlyDictionary<string, string> context,
        string reason,
        int? ruleIndex
    )
    {
        if (serve.Rollout is not { Count: > 0 } rollout)
            return new FlagEvaluation(serve.Variant ?? flag.DefaultVariant, reason, ruleIndex);

        // 5. Never random: no bucket-by value → the default variant.
        if (
            !context.TryGetValue(serve.EffectiveBucketBy, out var bucketBy)
            || string.IsNullOrEmpty(bucketBy)
        )
            return new FlagEvaluation(
                flag.DefaultVariant,
                EvaluationReasons.MissingBucketBy,
                ruleIndex
            );

        var bucket = Bucket(flag.Key, flag.Salt, bucketBy);
        return new FlagEvaluation(PickVariant(rollout, bucket), reason, ruleIndex, bucket);
    }

    /// <summary>
    /// Whether one condition matches. A missing attribute never matches (not even <c>notIn</c>);
    /// comparisons are ordinal and case-sensitive; list operators match when any value does.
    /// </summary>
    public static bool Matches(FlagCondition condition, IReadOnlyDictionary<string, string> context)
    {
        if (!context.TryGetValue(condition.Attribute, out var actual) || actual is null)
            return false;

        return condition.Operator switch
        {
            ConditionOperators.In or ConditionOperators.EqualTo => condition.Values.Contains(
                actual,
                StringComparer.Ordinal
            ),
            ConditionOperators.NotIn => !condition.Values.Contains(actual, StringComparer.Ordinal),
            ConditionOperators.Contains => condition.Values.Any(v =>
                actual.Contains(v, StringComparison.Ordinal)
            ),
            ConditionOperators.StartsWith => condition.Values.Any(v =>
                actual.StartsWith(v, StringComparison.Ordinal)
            ),
            ConditionOperators.EndsWith => condition.Values.Any(v =>
                actual.EndsWith(v, StringComparison.Ordinal)
            ),
            ConditionOperators.Matches => condition.Values.Any(v => RegexMatches(actual, v)),
            ConditionOperators.SemVerGte => CompareSemVer(actual, condition.Values) is >= 0,
            ConditionOperators.SemVerLt => CompareSemVer(actual, condition.Values) is < 0,
            _ => false,
        };
    }

    /// <summary>Whether <paramref name="pattern"/> is a usable regular expression.</summary>
    public static bool IsValidRegex(string? pattern)
    {
        if (pattern is null)
            return false;
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool RegexMatches(string actual, string pattern)
    {
        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static int? CompareSemVer(string actual, IReadOnlyList<string> values) =>
        values.Count > 0
        && SemVer.TryParse(actual, out var a)
        && SemVer.TryParse(values[0], out var b)
            ? a.CompareTo(b)
            : null;

    /// <summary>Converts a JSON-ish context value to the string the rules compare.</summary>
    public static string ToContextString(object? value) =>
        value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
}
