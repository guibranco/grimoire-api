using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.DTOs.Consumer;

/// <summary>
/// A flag definition for client-side evaluation in one environment. Grimoire never evaluates
/// on a consumer's behalf; see <c>docs/feature-flags/evaluation-spec.md</c>.
/// </summary>
public record ConsumerFeatureFlag(
    string Key,
    string Type,
    IReadOnlyList<FlagVariantDto> Variants,
    string DefaultVariant,
    string Salt,
    bool Enabled,
    string? OffVariant,
    IReadOnlyList<TargetingRuleDto> Rules,
    FlagServeDto? Fallthrough,
    int Version
);

public record ConsumerFeatureFlagList(string Environment, IReadOnlyList<ConsumerFeatureFlag> Flags);
