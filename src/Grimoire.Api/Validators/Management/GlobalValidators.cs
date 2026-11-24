using System.Text.RegularExpressions;
using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Grimoire.Core.Secrets;

namespace Grimoire.Api.Validators.Management;

public static partial class GlobalEnvironmentSlug
{
    public const int MaxLength = 100;

    /// <summary>"*" or a slug shaped like the ones SlugService generates.</summary>
    public static bool IsValid(string? slug) =>
        slug == GlobalConfigurationEntry.AllEnvironments
        || (slug is { Length: > 0 and <= MaxLength } && SlugPattern().IsMatch(slug));

    public const string Message =
        "'{PropertyName}' must be \"*\" or a lowercase slug (letters, digits and single hyphens).";

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public class CreateGlobalConfigurationRequestValidator
    : AbstractValidator<CreateGlobalConfigurationRequest>
{
    public CreateGlobalConfigurationRequestValidator()
    {
        RuleFor(x => x.EnvironmentSlug)
            .Must(GlobalEnvironmentSlug.IsValid)
            .WithMessage(GlobalEnvironmentSlug.Message)
            .When(x => x.EnvironmentSlug != null);
        RuleFor(x => x.Key).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Key)
            .Must(
                (x, k) =>
                    ConfigurationSentinel.ReservedKeyError(k, x.EnvironmentSlug ?? "*") is null
            )
            .WithMessage(x =>
                ConfigurationSentinel.ReservedKeyError(x.Key, x.EnvironmentSlug ?? "*")!
            )
            .When(x => !string.IsNullOrEmpty(x.Key));
        RuleFor(x => x.Value).NotNull();
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
        ConfigurationValueRules.AddRules(
            this,
            x => x.ValueType,
            x => x.Value,
            ConfigurationValueType.String
        );
    }
}

public class UpdateGlobalConfigurationRequestValidator
    : AbstractValidator<UpdateGlobalConfigurationRequest>
{
    public UpdateGlobalConfigurationRequestValidator()
    {
        RuleFor(x => x.Value).NotNull();
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
        ConfigurationValueRules.AddRules(this, x => x.ValueType, x => x.Value, null);
    }
}

public class CreateGlobalSecretRequestValidator : AbstractValidator<CreateGlobalSecretRequest>
{
    public CreateGlobalSecretRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
        RuleFor(x => x.RotationIntervalDays)
            .InclusiveBetween(1, SecretDueRules.MaxRotationIntervalDays)
            .When(x => x.RotationIntervalDays.HasValue);
    }
}

public class SetGlobalSecretValueRequestValidator : AbstractValidator<SetGlobalSecretValueRequest>
{
    public SetGlobalSecretValueRequestValidator()
    {
        RuleFor(x => x.EnvironmentSlug)
            .NotEmpty()
            .Must(GlobalEnvironmentSlug.IsValid)
            .WithMessage(GlobalEnvironmentSlug.Message);
        RuleFor(x => x.Value).NotEmpty();
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.NotBefore)
            .When(x => x.ExpiresAt.HasValue && x.NotBefore.HasValue);
        RuleFor(x => x.PreviousVersionGraceDays)
            .InclusiveBetween(0, SecretVersionRules.MaxGraceDays)
            .When(x => x.PreviousVersionGraceDays.HasValue);
    }
}

/// <summary>
/// The request body is a list, and auto-validation only resolves a validator for the root
/// type, so the per-item validator has to be applied explicitly.
/// </summary>
public class SetGlobalSecretValuesRequestValidator
    : AbstractValidator<List<SetGlobalSecretValueRequest>>
{
    public SetGlobalSecretValuesRequestValidator()
    {
        RuleForEach(x => x).SetValidator(new SetGlobalSecretValueRequestValidator());
    }
}
