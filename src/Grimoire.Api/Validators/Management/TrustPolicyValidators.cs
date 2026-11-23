using FluentValidation;
using Grimoire.Api.AccessTokens;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;

namespace Grimoire.Api.Validators.Management;

public class TrustPolicyRequestValidator : AbstractValidator<TrustPolicyRequest>
{
    public TrustPolicyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Repository)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(200)
            .Matches(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
            .WithMessage("Use owner/repo, e.g. guibranco/grimoire-api.");
        RuleFor(x => x.RefPattern)
            .Must(r => r!.StartsWith("refs/", StringComparison.Ordinal))
            .When(x => !string.IsNullOrEmpty(x.RefPattern))
            .WithMessage("Use a full ref, e.g. refs/heads/main or refs/tags/v*.")
            .MaximumLength(255);
        RuleFor(x => x.GitHubEnvironment).MaximumLength(255);
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
        RuleFor(x => x.TokenLifetimeMinutes)
            .InclusiveBetween(1, TrustPolicy.MaxLifetimeMinutes)
            .When(x => x.TokenLifetimeMinutes is not null);
    }
}
