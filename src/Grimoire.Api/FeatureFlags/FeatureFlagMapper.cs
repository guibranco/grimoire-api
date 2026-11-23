using System.Text.Json;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.FeatureFlags;

namespace Grimoire.Api.FeatureFlags;

/// <summary>Maps feature flags between entities, the core model and DTOs.</summary>
public static class FeatureFlagMapper
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    public static JsonElement ToJson(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    public static FlagVariantDto ToDto(FlagVariant v) => new(v.Key, ToJson(v.Value));

    public static FlagServeDto ToDto(FlagServe s) =>
        new(
            s.Variant,
            s.Rollout?.Select(r => new RolloutSliceDto(r.Variant, r.Weight)).ToList(),
            s.BucketBy
        );

    public static TargetingRuleDto ToDto(TargetingRule r) =>
        new(
            r.Conditions.Select(c => new FlagConditionDto(c.Attribute, c.Operator, [.. c.Values]))
                .ToList(),
            ToDto(r.Serve)
        );

    /// <summary><c>null</c> stays <c>null</c>.</summary>
    public static FlagServeDto? ToDtoOrNull(FlagServe? s) => s is null ? null : ToDto(s);

    public static FlagVariant ToModel(FlagVariantDto v) => new(v.Key, v.Value.GetRawText());

    public static FlagServe ToModel(FlagServeDto s) =>
        new(
            s.Variant,
            s.Rollout?.Select(r => new RolloutSlice(r.Variant, r.Weight)).ToList(),
            string.IsNullOrWhiteSpace(s.BucketBy) ? null : s.BucketBy
        );

    public static TargetingRule ToModel(TargetingRuleDto r) =>
        new(
            r.Conditions.Select(c => new FlagCondition(c.Attribute, c.Operator, [.. c.Values]))
                .ToList(),
            ToModel(r.Serve)
        );

    public static FlagEnvironmentResponse ToResponse(
        string environment,
        FeatureFlagEnvironmentState? state
    ) =>
        state is null
            ? new FlagEnvironmentResponse(environment, false, null, [], null, 0, null)
            : new FlagEnvironmentResponse(
                environment,
                state.Enabled,
                state.OffVariant,
                state.Rules.Select(ToDto).ToList(),
                ToDtoOrNull(state.Fallthrough),
                state.Version,
                state.UpdatedAt
            );

    /// <summary>The flag with one entry per environment of the scope, in order.</summary>
    public static FeatureFlagResponse ToResponse(FeatureFlag flag, FlagScope scope) =>
        new(
            flag.Key,
            flag.Description,
            flag.Type,
            flag.Variants.Select(ToDto).ToList(),
            flag.DefaultVariant,
            flag.Tags,
            flag.CreatedAt,
            flag.UpdatedAt,
            scope.Environments.Select(e => ToResponse(e.Slug, FlagScope.StateOf(flag, e))).ToList()
        );

    public static FeatureFlagHistoryEntryResponse ToResponse(FeatureFlagAuditEntry entry) =>
        new(
            entry.Change,
            entry.Environment,
            entry.Actor,
            entry.ChangedAt,
            entry.Before is null ? null : ToJson(entry.Before),
            entry.After is null ? null : ToJson(entry.After)
        );

    /// <param name="flag">The flag.</param>
    /// <param name="state">Its effective state in the consumer's environment, if any.</param>
    public static ConsumerFeatureFlag ToConsumer(
        FeatureFlag flag,
        FeatureFlagEnvironmentState? state
    ) =>
        new(
            flag.Key,
            flag.Type,
            flag.Variants.Select(ToDto).ToList(),
            flag.DefaultVariant,
            flag.Salt,
            state?.Enabled ?? false,
            state?.OffVariant,
            state?.Rules.Select(ToDto).ToList() ?? [],
            ToDtoOrNull(state?.Fallthrough),
            state?.Version ?? 0,
            flag.IsGlobal ? "global" : "application"
        );

    /// <summary>The flag itself (no environment states) as stored in the audit trail.</summary>
    public static string Snapshot(FeatureFlag flag) =>
        JsonSerializer.Serialize(
            new
            {
                flag.Key,
                flag.Description,
                flag.Type,
                Variants = flag.Variants.Select(ToDto),
                flag.DefaultVariant,
                flag.Tags,
            },
            SnapshotOptions
        );

    /// <summary>One environment state as stored in the audit trail; <c>null</c> when never configured.</summary>
    public static string? Snapshot(FeatureFlagEnvironmentState? state) =>
        state is null
            ? null
            : JsonSerializer.Serialize(
                new
                {
                    state.Enabled,
                    state.OffVariant,
                    Rules = state.Rules.Select(ToDto),
                    Fallthrough = ToDtoOrNull(state.Fallthrough),
                    state.Version,
                },
                SnapshotOptions
            );

    /// <summary>Context values as the rules compare them: strings, numbers and booleans as text.</summary>
    public static Dictionary<string, string> ToContext(Dictionary<string, JsonElement>? context)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in context ?? [])
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    result[key] = value.GetString()!;
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    result[key] = value.GetRawText();
                    break;
            }
        }
        return result;
    }
}
