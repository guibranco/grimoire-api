using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Secrets;

namespace Grimoire.Api.Validators.Management;

public class CreateSecretRequestValidator : AbstractValidator<CreateSecretRequest>
{
    public CreateSecretRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
        RuleFor(x => x.RotationIntervalDays)
            .InclusiveBetween(1, SecretDueRules.MaxRotationIntervalDays)
            .When(x => x.RotationIntervalDays.HasValue);
    }
}

public class UpdateSecretRequestValidator : AbstractValidator<UpdateSecretRequest>
{
    public UpdateSecretRequestValidator()
    {
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
        RuleFor(x => x.RotationIntervalDays)
            .InclusiveBetween(1, SecretDueRules.MaxRotationIntervalDays)
            .When(x => x.RotationIntervalDays.HasValue);
        RuleFor(x => x.Description)
            .Null()
            .When(x => x.ClearDescription)
            .WithMessage("Use either 'description' or 'clearDescription', not both.");
        RuleFor(x => x.RotationIntervalDays)
            .Null()
            .When(x => x.ClearRotationInterval)
            .WithMessage("Use either 'rotationIntervalDays' or 'clearRotationInterval', not both.");
    }
}

public class SetSecretValueRequestValidator : AbstractValidator<SetSecretValueRequest>
{
    public SetSecretValueRequestValidator()
    {
        RuleFor(x => x.EnvironmentSlug).NotEmpty();
        RuleFor(x => x.Value).NotEmpty();
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.NotBefore)
            .When(x => x.ExpiresAt.HasValue && x.NotBefore.HasValue);
        RuleFor(x => x.PreviousVersionGraceDays)
            .InclusiveBetween(0, SecretVersionRules.MaxGraceDays)
            .When(x => x.PreviousVersionGraceDays.HasValue);
    }
}

public class UpdateSecretVersionRequestValidator : AbstractValidator<UpdateSecretVersionRequest>
{
    public UpdateSecretVersionRequestValidator()
    {
        RuleFor(x => x.NotBefore)
            .Null()
            .When(x => x.ClearNotBefore)
            .WithMessage("Use either 'notBefore' or 'clearNotBefore', not both.");
        RuleFor(x => x.ExpiresAt)
            .Null()
            .When(x => x.ClearExpiresAt)
            .WithMessage("Use either 'expiresAt' or 'clearExpiresAt', not both.");
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => x.NotBefore)
            .When(x => x.ExpiresAt.HasValue && x.NotBefore.HasValue);
    }
}

/// <summary>
/// The request body is a list, and auto-validation only resolves a validator for the root
/// type, so the per-item validator has to be applied explicitly.
/// </summary>
public class SetSecretValuesRequestValidator : AbstractValidator<List<SetSecretValueRequest>>
{
    public SetSecretValuesRequestValidator()
    {
        RuleForEach(x => x).SetValidator(new SetSecretValueRequestValidator());
    }
}
