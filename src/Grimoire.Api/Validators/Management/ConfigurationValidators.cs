using FluentValidation;
using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.Validators.Management;

public class CreateConfigurationRequestValidator : AbstractValidator<CreateConfigurationRequest>
{
    public CreateConfigurationRequestValidator()
    {
        RuleFor(x => x.EnvironmentSlug).NotEmpty();
        RuleFor(x => x.Key).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Value).NotNull();
    }
}

public class UpdateConfigurationRequestValidator : AbstractValidator<UpdateConfigurationRequest>
{
    public UpdateConfigurationRequestValidator()
    {
        RuleFor(x => x.Value).NotNull();
    }
}
