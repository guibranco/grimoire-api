using System.Text.Json;

namespace Grimoire.Consumer.FeatureFlags;

/// <summary>Why a flag result is what it is.</summary>
public static class FeatureFlagReasons
{
    /// <summary>The flag is off in the environment.</summary>
    public const string Off = "OFF";

    /// <summary>A targeting rule matched (see <see cref="FeatureFlagResult.MatchedRuleIndex"/>).</summary>
    public const string RuleMatch = "RULE_MATCH";

    /// <summary>No rule matched: the default rule.</summary>
    public const string Fallthrough = "FALLTHROUGH";

    /// <summary>A rollout needed the bucket-by attribute and the context lacks it.</summary>
    public const string MissingBucketBy = "MISSING_BUCKET_BY";

    /// <summary>No flag with this key (or definitions could not be loaded yet).</summary>
    public const string FlagNotFound = "FLAG_NOT_FOUND";
}

/// <param name="Key">The flag key.</param>
/// <param name="Variant">The variant served; <c>null</c> when the flag is unknown.</param>
/// <param name="Value">The variant's JSON value; <c>Undefined</c> when the flag is unknown.</param>
/// <param name="Reason">One of <see cref="FeatureFlagReasons"/>.</param>
/// <param name="MatchedRuleIndex">Zero-based rule index for <c>RULE_MATCH</c>.</param>
/// <param name="Bucket">0–9999 when a rollout bucketed the context.</param>
public sealed record FeatureFlagResult(
    string Key,
    string? Variant,
    JsonElement Value,
    string Reason,
    int? MatchedRuleIndex = null,
    int? Bucket = null
)
{
    /// <summary>Whether the flag exists (definitions loaded and the key known).</summary>
    public bool Found => Variant is not null;

    /// <summary><c>true</c> when the served value is the JSON <c>true</c> (a boolean flag's <c>on</c>).</summary>
    public bool IsEnabled => Value.ValueKind == JsonValueKind.True;

    /// <summary>The value deserialised to <typeparamref name="T"/>, or <paramref name="fallback"/>.</summary>
    public T? ValueAs<T>(T? fallback = default)
    {
        if (!Found || Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return fallback;
        try
        {
            return Value.Deserialize<T>(GrimoireFeatureFlagClient.JsonOptions);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
