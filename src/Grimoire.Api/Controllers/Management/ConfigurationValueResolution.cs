using Grimoire.Core.Entities;
using Grimoire.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

internal static class ConfigurationValueResolution
{
    /// <summary>
    /// Picks the value type (the requested one, or <paramref name="fallback"/> when the request
    /// leaves it out) and normalises the value for it. On failure the error is added to
    /// <see cref="ControllerBase.ModelState"/> so the caller can return a validation problem.
    /// </summary>
    public static bool TryResolveConfigurationValue(
        this ControllerBase controller,
        string? requestedType,
        ConfigurationValueType fallback,
        string value,
        out ConfigurationValueType valueType,
        out string normalized
    )
    {
        normalized = value;
        valueType = fallback;

        if (
            requestedType is not null
            && !ConfigurationValueNormalizer.TryParseName(requestedType, out valueType)
        )
        {
            controller.ModelState.AddModelError(
                "ValueType",
                "'Value Type' must be one of: string, multiline, array, json."
            );
            return false;
        }

        if (
            !ConfigurationValueNormalizer.TryNormalize(
                valueType,
                value,
                out var result,
                out var error
            )
        )
        {
            controller.ModelState.AddModelError("Value", error);
            return false;
        }

        normalized = result;
        return true;
    }
}
