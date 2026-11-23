using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Import;

namespace Grimoire.Api.Validators.Management;

public class ImportRequestValidator : AbstractValidator<ImportRequest>
{
    /// <summary>1 MiB of text is far more than any config file.</summary>
    public const int MaxContentLength = 1024 * 1024;

    public static readonly string[] Conflicts = ["skip", "overwrite"];
    public static readonly string[] Kinds = ["secret", "configuration"];

    public ImportRequestValidator()
    {
        RuleFor(x => x.Format)
            .Must(f => ImportParser.TryParseFormat(f, out _))
            .WithMessage("'{PropertyName}' must be one of: env, appsettings, php, json.");
        RuleFor(x => x.Content).NotEmpty().MaximumLength(MaxContentLength);
        RuleFor(x => x.OnConflict)
            .Must(c => Conflicts.Contains(c, StringComparer.OrdinalIgnoreCase))
            .When(x => x.OnConflict is not null)
            .WithMessage("'{PropertyName}' must be skip or overwrite.");
        RuleForEach(x => x.SecretKeys).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SecretKeys).Must(k => k!.Count <= 50).When(x => x.SecretKeys is not null);
        RuleForEach(x => x.Kinds)
            .Must(k => Kinds.Contains(k.Value, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Kinds is not null)
            .WithMessage("Each kind must be secret or configuration.");
    }
}
