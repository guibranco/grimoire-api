using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.FeatureFlags;

namespace Grimoire.Api.Validators.Management;

/// <summary>Rules shared by the feature flag validators and controller.</summary>
public static partial class FeatureFlagRules
{
    public const int KeyMaxLength = 500;
    public const int VariantKeyMaxLength = 100;
    public const int MaxVariants = 20;
    public const int MaxRules = 100;
    public const int MaxConditions = 20;
    public const int MaxValues = 500;
    public const int MaxTags = 20;
    public const int TagMaxLength = 50;

    /// <summary>Letters, digits and <c>. _ : -</c>, starting with a letter or digit (safe in URLs).</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    public static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    public static partial Regex VariantKeyPattern();

    public const string KeyMessage =
        "'{PropertyName}' may contain letters, digits, '.', '_', ':' and '-', and must start with a letter or digit.";

    public static bool HasAtMostTwoDecimals(decimal weight) =>
        weight * 100 == Math.Truncate(weight * 100);

    public static bool IsJsonValue(JsonElement value) => value.ValueKind != JsonValueKind.Undefined;

    /// <summary>Variant keys a state refers to that the flag doesn't have.</summary>
    public static IEnumerable<string> UnknownVariants(
        FlagEnvironmentStateRequest state,
        IReadOnlySet<string> variants
    )
    {
        IEnumerable<string?> serveVariants(FlagServeDto? s) =>
            s is null ? [] : [s.Variant, .. (s.Rollout ?? []).Select(r => r.Variant)];

        return new[] { state.OffVariant }
            .Concat(serveVariants(state.Fallthrough))
            .Concat((state.Rules ?? []).SelectMany(r => serveVariants(r.Serve)))
            .OfType<string>()
            .Where(v => !variants.Contains(v))
            .Distinct();
    }
}

internal static class FeatureFlagValidationExtensions
{
    public static IRuleBuilderOptions<T, string> FlagKey<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(FeatureFlagRules.KeyMaxLength)
            .Matches(FeatureFlagRules.KeyPattern())
            .WithMessage(FeatureFlagRules.KeyMessage);

    /// <summary>Variants of a multivariate flag: 2–20, valid unique keys, JSON values.</summary>
    public static void VariantRules<T>(
        this AbstractValidator<T> validator,
        Func<T, List<FlagVariantDto>?> variants,
        Func<T, bool> applies
    )
    {
        validator
            .RuleFor(x => variants(x))
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithName("Variants")
            .Must(v => v!.Count is >= 2 and <= FeatureFlagRules.MaxVariants)
            .WithMessage($"A multivariate flag needs 2 to {FeatureFlagRules.MaxVariants} variants.")
            .Must(v => v!.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == v!.Count)
            .WithMessage("Variant keys must be unique.")
            .When(applies);

        validator
            .RuleForEach(x => variants(x))
            .ChildRules(v =>
            {
                v.RuleFor(x => x.Key)
                    .NotEmpty()
                    .MaximumLength(FeatureFlagRules.VariantKeyMaxLength)
                    .Matches(FeatureFlagRules.VariantKeyPattern())
                    .WithMessage("'Key' may contain letters, digits, '.', '_' and '-'.");
                v.RuleFor(x => x.Value)
                    .Must(FeatureFlagRules.IsJsonValue)
                    .WithMessage("'Value' must be a JSON value.");
            })
            .OverridePropertyName("Variants")
            .When(applies);
    }

    public static void TagRules<T>(
        this AbstractValidator<T> validator,
        Func<T, List<string>?> tags
    ) =>
        validator
            .RuleFor(x => tags(x))
            .Must(t => t is null || t.Count <= FeatureFlagRules.MaxTags)
            .WithMessage($"At most {FeatureFlagRules.MaxTags} tags.")
            .Must(t =>
                t is null
                || t.All(x =>
                    !string.IsNullOrWhiteSpace(x) && x.Length <= FeatureFlagRules.TagMaxLength
                )
            )
            .WithMessage(
                $"Tags must be non-empty and at most {FeatureFlagRules.TagMaxLength} characters."
            )
            .WithName("Tags");
}

public class CreateFeatureFlagRequestValidator : AbstractValidator<CreateFeatureFlagRequest>
{
    public CreateFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Key).FlagKey();
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Type)
            .Must(FlagTypes.IsKnown)
            .WithMessage("'Type' must be 'boolean' or 'multivariate'.")
            .When(x => x.Type is not null);
        RuleFor(x => x.Variants)
            .Null()
            .WithMessage(
                "Boolean flags have the fixed variants 'on' and 'off'; leave 'Variants' out."
            )
            .When(x => (x.Type ?? FlagTypes.Boolean) == FlagTypes.Boolean);
        this.VariantRules(x => x.Variants, x => x.Type == FlagTypes.Multivariate);
        this.TagRules(x => x.Tags);
    }
}

public class UpdateFeatureFlagRequestValidator : AbstractValidator<UpdateFeatureFlagRequest>
{
    public UpdateFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Description).MaximumLength(1000);
        // Whether variants are allowed depends on the stored type: checked by the controller.
        this.VariantRules(x => x.Variants, x => x.Variants is not null);
        this.TagRules(x => x.Tags);
    }
}

public class FlagServeDtoValidator : AbstractValidator<FlagServeDto>
{
    public FlagServeDtoValidator()
    {
        RuleFor(x => x)
            .Must(s => (s.Variant is not null) ^ (s.Rollout is not null))
            .WithMessage("Serve either a 'Variant' or a 'Rollout', not both.")
            .WithName("Serve");
        RuleFor(x => x.Rollout!)
            .Must(r => r.Count > 0)
            .WithMessage("'Rollout' needs at least one variant.")
            .Must(r =>
                r.All(s =>
                    s.Weight is >= 0 and <= 100 && FeatureFlagRules.HasAtMostTwoDecimals(s.Weight)
                )
            )
            .WithMessage("Rollout weights must be between 0 and 100 with at most two decimals.")
            .Must(r => r.Sum(s => s.Weight) == 100)
            .WithMessage("Rollout weights must sum to 100.")
            .Must(r => r.Select(s => s.Variant).Distinct(StringComparer.Ordinal).Count() == r.Count)
            .WithMessage("Each variant may appear once in a rollout.")
            .When(x => x.Rollout is not null);
        RuleFor(x => x.BucketBy).MaximumLength(100);
    }
}

public class FlagConditionDtoValidator : AbstractValidator<FlagConditionDto>
{
    public FlagConditionDtoValidator()
    {
        RuleFor(x => x.Attribute).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operator)
            .Must(ConditionOperators.IsKnown)
            .WithMessage(
                $"'Operator' must be one of: {string.Join(", ", ConditionOperators.All)}."
            );
        RuleFor(x => x.Values)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(v => v.Count is > 0 and <= FeatureFlagRules.MaxValues)
            .WithMessage($"'Values' needs 1 to {FeatureFlagRules.MaxValues} values.");
        RuleFor(x => x.Values)
            .Must(v => v.Count == 1)
            .WithMessage("Semantic version operators take exactly one value.")
            .Must(v => v.All(x => SemVer.TryParse(x, out _)))
            .WithMessage("Values must be semantic versions, e.g. 1.4.0.")
            .When(x => ConditionOperators.IsSingleValue(x.Operator) && x.Values is { Count: > 0 });
        RuleForEach(x => x.Values)
            .Must(FlagEvaluator.IsValidRegex)
            .WithMessage("'{PropertyValue}' is not a valid regular expression.")
            .When(x => x.Operator == ConditionOperators.Matches && x.Values is not null);
    }
}

public class FlagEnvironmentStateRequestValidator : AbstractValidator<FlagEnvironmentStateRequest>
{
    public FlagEnvironmentStateRequestValidator()
    {
        RuleFor(x => x.Rules)
            .Must(r => r is null || r.Count <= FeatureFlagRules.MaxRules)
            .WithMessage($"At most {FeatureFlagRules.MaxRules} rules.");
        RuleForEach(x => x.Rules)
            .ChildRules(rule =>
            {
                rule.RuleFor(r => r.Conditions)
                    .Cascade(CascadeMode.Stop)
                    .NotNull()
                    .Must(c => c.Count is > 0 and <= FeatureFlagRules.MaxConditions)
                    .WithMessage($"A rule needs 1 to {FeatureFlagRules.MaxConditions} conditions.");
                rule.RuleForEach(r => r.Conditions).SetValidator(new FlagConditionDtoValidator());
                rule.RuleFor(r => r.Serve).NotNull().SetValidator(new FlagServeDtoValidator());
            });
        RuleFor(x => x.Fallthrough!)
            .SetValidator(new FlagServeDtoValidator())
            .When(x => x.Fallthrough is not null);
    }
}

public class EvaluateFeatureFlagRequestValidator : AbstractValidator<EvaluateFeatureFlagRequest>
{
    public EvaluateFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Environment).NotEmpty();
    }
}
