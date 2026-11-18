using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using Grimoire.Core.Entities;

namespace Grimoire.Core.Services;

/// <summary>
/// Validates a configuration value against its <see cref="ConfigurationValueType"/> and turns it
/// into the canonical form that is stored, so diffs and comparisons stay stable.
/// </summary>
public static class ConfigurationValueNormalizer
{
    // Keep non-ASCII text and characters such as '+' readable in the stored value; it is data,
    // never embedded in HTML.
    private static readonly JsonSerializerOptions StorageOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Wire names for <see cref="ConfigurationValueType"/>, as used by the API.</summary>
    public static readonly IReadOnlyDictionary<string, ConfigurationValueType> Names =
        new Dictionary<string, ConfigurationValueType>(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = ConfigurationValueType.String,
            ["multiline"] = ConfigurationValueType.Multiline,
            ["array"] = ConfigurationValueType.Array,
            ["json"] = ConfigurationValueType.Json,
        };

    public static string ToName(ConfigurationValueType type) =>
        type switch
        {
            ConfigurationValueType.Multiline => "multiline",
            ConfigurationValueType.Array => "array",
            ConfigurationValueType.Json => "json",
            _ => "string",
        };

    public static bool TryParseName(string? name, out ConfigurationValueType type) =>
        Names.TryGetValue(name ?? string.Empty, out type);

    /// <summary>
    /// Checks <paramref name="value"/> for <paramref name="type"/> and returns the value to store.
    /// <list type="bullet">
    /// <item><c>String</c>: stored as given.</item>
    /// <item><c>Multiline</c>: line endings normalised to <c>\n</c>.</item>
    /// <item><c>Array</c>: a JSON array of strings, or one item per line (blank lines and
    /// surrounding whitespace ignored); stored as a minified JSON array.</item>
    /// <item><c>Json</c>: any JSON object or array; stored minified.</item>
    /// </list>
    /// </summary>
    public static bool TryNormalize(
        ConfigurationValueType type,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error
    )
    {
        normalized = null;
        error = null;
        switch (type)
        {
            case ConfigurationValueType.String:
                normalized = value;
                return true;
            case ConfigurationValueType.Multiline:
                normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
                return true;
            case ConfigurationValueType.Array:
                return TryNormalizeArray(value, out normalized, out error);
            case ConfigurationValueType.Json:
                return TryNormalizeJson(value, out normalized, out error);
            default:
                error = "Unknown value type.";
                return false;
        }
    }

    private static bool TryNormalizeArray(
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error
    )
    {
        normalized = null;
        error = null;
        var trimmed = value.Trim();

        List<string> items;
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                items = [];
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        error = "An array value must contain only strings.";
                        return false;
                    }
                    items.Add(element.GetString()!);
                }
            }
            catch (JsonException ex)
            {
                error = $"An array value must be a JSON array of strings: {ex.Message}";
                return false;
            }
        }
        else
        {
            // One item per line, which is how lists such as IP allow-lists are usually pasted.
            items = trimmed
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .ToList();
        }

        normalized = JsonSerializer.Serialize(items, StorageOptions);
        return true;
    }

    private static bool TryNormalizeJson(
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error
    )
    {
        normalized = null;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                error = "A JSON value must be an object or an array.";
                return false;
            }
            normalized = JsonSerializer.Serialize(doc.RootElement, StorageOptions);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The value is not valid JSON: {ex.Message}";
            return false;
        }
    }
}
