using FluentValidation;
using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.Validators.Management;

public class CreateSecretRequestValidator : AbstractValidator<CreateSecretRequest>
{
    public CreateSecretRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description != null);
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
    }
}
