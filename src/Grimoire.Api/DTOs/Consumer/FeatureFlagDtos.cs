using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.DTOs.Consumer;

/// <summary>
/// A flag definition for client-side evaluation in one environment. Grimoire never evaluates
/// on a consumer's behalf; see <c>docs/feature-flags/evaluation-spec.md</c>.
/// </summary>
/// <param name="Source"><c>application</c>, or <c>global</c> for an inherited global flag.</param>
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
    int Version,
    string Source = "application"
);

public record ConsumerFeatureFlagList(string Environment, IReadOnlyList<ConsumerFeatureFlag> Flags);
