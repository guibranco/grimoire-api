using FluentValidation;
using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.Validators.Management;

public class CreateEnvironmentRequestValidator : AbstractValidator<CreateEnvironmentRequest>
{
    public CreateEnvironmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
