using FluentValidation;
using Grimoire.Api.AccessTokens;
using Grimoire.Api.DTOs.Management;

namespace Grimoire.Api.Validators.Management;

public class CreateAccessTokenRequestValidator : AbstractValidator<CreateAccessTokenRequest>
{
    public CreateAccessTokenRequestValidator(TimeProvider time)
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Scopes)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Choose at least one scope.")
            .Must(s => s.All(AccessTokenScopeNames.IsKnown))
            .WithMessage($"Scopes must be among: {string.Join(", ", AccessTokenScopeNames.All)}.");
        RuleFor(x => x.Environments)
            .Must(e => e!.Count > 0)
            .When(x => x.Environments is not null)
            .WithMessage("List at least one environment, or omit environments for all of them.");
        RuleForEach(x => x.Environments).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExpiresAt)
            .Must(e => e > time.GetUtcNow())
            .When(x => x.ExpiresAt is not null)
            .WithMessage("The expiry must be in the future.");
    }
}
