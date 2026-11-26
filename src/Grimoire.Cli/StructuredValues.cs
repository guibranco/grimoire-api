using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grimoire.Cli;

/// <summary>How <c>array</c> and <c>json</c> configuration values are written to flat outputs.</summary>
internal enum StructuredMode
{
    /// <summary>The stored JSON text under the key itself (default, matches the GitHub Action).</summary>
    Raw,

    /// <summary>One entry per leaf, the way <c>IConfiguration</c> reads JSON: <c>AllowedIps:0</c>.</summary>
    Flatten,

    /// <summary>The raw value and the flattened entries.</summary>
    Both,
}

/// <summary>
/// Value-type handling for exports (#99), following the <c>Grimoire.Consumer</c> rules: arrays are
/// indexed from 0, nested objects are joined with <c>:</c>, numbers and booleans keep their JSON
/// text, <c>null</c> is empty, and an empty array or object gives no entries. Invalid JSON falls
/// back to the raw text with a warning.
/// </summary>
internal static class StructuredValues
{
    public static readonly string[] Modes = ["raw", "flatten", "both"];

    public static StructuredMode ParseMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "flatten" => StructuredMode.Flatten,
            "both" => StructuredMode.Both,
            _ => StructuredMode.Raw,
        };

    public static bool IsStructured(string? valueType) => valueType is "array" or "json";

    /// <summary>The configuration values with structured ones expanded per <paramref name="mode"/>.</summary>
    public static Dictionary<string, string> Expand(
        IReadOnlyDictionary<string, string> configurations,
        IReadOnlyDictionary<string, string> valueTypes,
        StructuredMode mode,
        Action<string> warn
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in configurations)
        {
            var structured =
                mode != StructuredMode.Raw && IsStructured(valueTypes.GetValueOrDefault(key));
            var node = structured ? Parse(key, value, warn) : null;
            if (node is null || mode == StructuredMode.Both)
                result[key] = value;
            if (node is not null)
                Flatten(key, node, result);
        }
        return result;
    }

    /// <summary>
    /// The configuration values as a JSON object, with <c>array</c> and <c>json</c> values as real
    /// JSON so the output round-trips through the import.
    /// </summary>
    public static JsonObject ToJsonObject(
        IReadOnlyDictionary<string, string> configurations,
        IReadOnlyDictionary<string, string> valueTypes,
        Action<string> warn
    )
    {
        var result = new JsonObject();
        foreach (var (key, value) in configurations.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var node = IsStructured(valueTypes.GetValueOrDefault(key))
                ? Parse(key, value, warn)
                : null;
            result[key] = node ?? JsonValue.Create(value);
        }
        return result;
    }

    private static JsonNode? Parse(string key, string value, Action<string> warn)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            warn($"'{key}' is not valid JSON; exported as text.");
            return null;
        }
    }

    private static void Flatten(string prefix, JsonNode? node, Dictionary<string, string> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                    Flatten($"{prefix}:{name}", child, result);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Flatten($"{prefix}:{i}", array[i], result);
                break;
            case JsonValue leaf when leaf.GetValueKind() == JsonValueKind.String:
                result[prefix] = leaf.GetValue<string>();
                break;
            case null:
                result[prefix] = "";
                break;
            default:
                result[prefix] = node.ToJsonString();
                break;
        }
    }
}
