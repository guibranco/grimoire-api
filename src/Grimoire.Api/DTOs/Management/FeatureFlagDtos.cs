using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Api.DTOs.Management;

/// <param name="Key">Variant key, e.g. <c>control</c>, <c>treatment</c>.</param>
/// <param name="Value">Any JSON value: <c>true</c>, <c>"blue"</c>, <c>{"limit":5}</c>.</param>
public record FlagVariantDto(string Key, JsonElement Value);

/// <param name="Weight">Percentage, 0–100 with up to two decimals; a rollout's weights sum to 100.</param>
public record RolloutSliceDto(string Variant, decimal Weight);

/// <summary>Serve a fixed <paramref name="Variant"/> or a weighted <paramref name="Rollout"/>, not both.</summary>
/// <param name="BucketBy">Context attribute a rollout buckets by (default <c>userId</c>).</param>
public record FlagServeDto(
    string? Variant = null,
    List<RolloutSliceDto>? Rollout = null,
    string? BucketBy = null
);

/// <param name="Operator"><c>in</c>, <c>notIn</c>, <c>equals</c>, <c>contains</c>, <c>startsWith</c>, <c>endsWith</c>, <c>matches</c>, <c>semVerGte</c> or <c>semVerLt</c>.</param>
public record FlagConditionDto(string Attribute, string Operator, List<string> Values);

/// <summary>All conditions must match (AND).</summary>
public record TargetingRuleDto(List<FlagConditionDto> Conditions, FlagServeDto Serve);

/// <param name="Type"><c>boolean</c> (default; variants <c>on</c>/<c>off</c>) or <c>multivariate</c>.</param>
/// <param name="Variants">Multivariate only: two or more, unique keys.</param>
/// <param name="DefaultVariant">Served when disabled without an off variant, with no fallthrough, or without a bucket-by value. Default <c>off</c> for boolean flags, the first variant otherwise.</param>
public record CreateFeatureFlagRequest(
    string Key,
    string? Description = null,
    string? Type = null,
    List<FlagVariantDto>? Variants = null,
    string? DefaultVariant = null,
    List<string>? Tags = null
);

/// <summary>Replaces the flag's description, tags and (multivariate) variants. The type can't change.</summary>
public record UpdateFeatureFlagRequest(
    string? Description = null,
    List<FlagVariantDto>? Variants = null,
    string? DefaultVariant = null,
    List<string>? Tags = null
);

/// <summary>Replaces the flag's state in one environment.</summary>
/// <param name="OffVariant">Served while disabled; the default variant when omitted.</param>
/// <param name="Rules">Evaluated in order; the first whose conditions all match wins.</param>
/// <param name="Fallthrough">Served when no rule matches; the default variant when omitted.</param>
public record FlagEnvironmentStateRequest(
    [property: JsonRequired] bool Enabled,
    string? OffVariant = null,
    List<TargetingRuleDto>? Rules = null,
    FlagServeDto? Fallthrough = null
);

/// <param name="Enabled">The new state; omit to flip it.</param>
public record ToggleFeatureFlagRequest(bool? Enabled = null);

/// <param name="Environment">Environment slug whose state is evaluated.</param>
/// <param name="Context">Attributes, e.g. <c>{"userId":"u-1","country":"IE"}</c>. Not stored.</param>
public record EvaluateFeatureFlagRequest(
    string Environment,
    Dictionary<string, JsonElement>? Context = null
);

public record FeatureFlagResponse(
    string Key,
    string? Description,
    string Type,
    IReadOnlyList<FlagVariantDto> Variants,
    string DefaultVariant,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FlagEnvironmentResponse> Environments
);

/// <summary>A flag's state in one environment. <paramref name="Version"/> 0 means never configured.</summary>
public record FlagEnvironmentResponse(
    string Environment,
    bool Enabled,
    string? OffVariant,
    IReadOnlyList<TargetingRuleDto> Rules,
    FlagServeDto? Fallthrough,
    int Version,
    DateTimeOffset? UpdatedAt
);

/// <param name="Reason"><c>OFF</c>, <c>RULE_MATCH</c>, <c>FALLTHROUGH</c> or <c>MISSING_BUCKET_BY</c>.</param>
/// <param name="MatchedRuleIndex">Zero-based, for <c>RULE_MATCH</c>.</param>
/// <param name="Bucket">0–9999 when a rollout bucketed the context.</param>
public record FlagEvaluationResponse(
    string Variant,
    JsonElement Value,
    string Reason,
    int? MatchedRuleIndex,
    int? Bucket
);

/// <param name="Change"><c>create</c>, <c>update</c>, <c>environment</c>, <c>toggle</c> or <c>delete</c>.</param>
/// <param name="Environment">Environment slug for environment changes.</param>
public record FeatureFlagHistoryEntryResponse(
    string Change,
    string? Environment,
    string Actor,
    DateTimeOffset ChangedAt,
    JsonElement? Before,
    JsonElement? After
);
