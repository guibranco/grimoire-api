using System.Linq.Expressions;
using FluentValidation;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Grimoire.Core.Services;

namespace Grimoire.Api.Validators.Management;

/// <summary>Shared rules for a configuration value and its value type.</summary>
public static class ConfigurationValueRules
{
    public const string ValueTypeMessage =
        "'{PropertyName}' must be one of: string, multiline, array, json.";

    public static bool IsKnownValueType(string? valueType) =>
        ConfigurationValueNormalizer.TryParseName(valueType, out _);

    /// <summary>
    /// Adds the value-type rule and, when the value type is known from the request, checks the
    /// value against it. A value whose type comes from the stored entry (an update that leaves
    /// the type out) is checked by the controller.
    /// </summary>
    public static void AddRules<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string?>> valueTypeProperty,
        Expression<Func<T, string?>> valueProperty,
        ConfigurationValueType? defaultType
    )
    {
        var valueType = valueTypeProperty.Compile();

        validator
            .RuleFor(valueTypeProperty)
            .Must(IsKnownValueType)
            .WithMessage(ValueTypeMessage)
            .When(x => valueType(x) != null);

        validator
            .RuleFor(valueProperty)
            .Custom(
                (v, context) =>
                {
                    if (v is null)
                        return;
                    var name = valueType(context.InstanceToValidate);
                    ConfigurationValueType type;
                    if (name is null)
                    {
                        if (defaultType is null)
                            return;
                        type = defaultType.Value;
                    }
                    else if (!ConfigurationValueNormalizer.TryParseName(name, out type))
                    {
                        return;
                    }

                    if (!ConfigurationValueNormalizer.TryNormalize(type, v, out _, out var error))
                        context.AddFailure(error);
                }
            );
    }
}

public class CreateConfigurationRequestValidator : AbstractValidator<CreateConfigurationRequest>
{
    public CreateConfigurationRequestValidator()
    {
        RuleFor(x => x.EnvironmentSlug).NotEmpty();
        RuleFor(x => x.Key).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Key)
            .Must(k => ConfigurationSentinel.ReservedKeyError(k, null) is null)
            .WithMessage(x => ConfigurationSentinel.ReservedKeyError(x.Key, null)!)
            .When(x => !string.IsNullOrEmpty(x.Key));
        RuleFor(x => x.Value).NotNull();
        ConfigurationValueRules.AddRules(
            this,
            x => x.ValueType,
            x => x.Value,
            ConfigurationValueType.String
        );
    }
}

public class UpdateConfigurationRequestValidator : AbstractValidator<UpdateConfigurationRequest>
{
    public UpdateConfigurationRequestValidator()
    {
        RuleFor(x => x.Value).NotNull();
        ConfigurationValueRules.AddRules(this, x => x.ValueType, x => x.Value, null);
    }
}
