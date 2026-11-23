using System.Text.Encodings.Web;
using System.Text.Json;
using Grimoire.Core.Entities;

namespace Grimoire.Core.Import;

/// <summary>
/// JSON objects. With <c>flatten</c> (appsettings.json), nested objects become
/// <c>Section:Key</c>; otherwise only the top level is read and objects stay JSON values.
/// Arrays of strings, numbers and booleans become array values; other arrays stay JSON.
/// </summary>
public static class JsonImportParser
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<ImportedValue> Parse(string content, bool flatten)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content, Lenient);
        }
        catch (JsonException ex)
        {
            throw new ImportParseException($"Invalid JSON: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ImportParseException("Expected a JSON object at the top level.");
            var values = new List<ImportedValue>();
            Walk(doc.RootElement, null, flatten, values);
            return values;
        }
    }

    private static void Walk(
        JsonElement obj,
        string? prefix,
        bool flatten,
        List<ImportedValue> values
    )
    {
        foreach (var p in obj.EnumerateObject())
        {
            var key = prefix is null ? p.Name : $"{prefix}:{p.Name}";
            if (flatten && p.Value.ValueKind == JsonValueKind.Object)
                Walk(p.Value, key, flatten, values);
            else
                values.Add(ToValue(key, p.Value));
        }
    }

    internal static ImportedValue ToValue(string key, JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => Text(key, value.GetString()!),
            JsonValueKind.Null => new ImportedValue(key, "", ConfigurationValueType.String),
            JsonValueKind.Array when value.EnumerateArray().All(IsScalar) => new ImportedValue(
                key,
                JsonSerializer.Serialize(value.EnumerateArray().Select(ScalarText), Compact),
                ConfigurationValueType.Array
            ),
            JsonValueKind.Array or JsonValueKind.Object => new ImportedValue(
                key,
                JsonSerializer.Serialize(value, Compact),
                ConfigurationValueType.Json
            ),
            _ => new ImportedValue(key, value.GetRawText(), ConfigurationValueType.String),
        };

    private static ImportedValue Text(string key, string text) =>
        new(key, text, ImportParser.TextType(text));

    private static bool IsScalar(JsonElement e) =>
        e.ValueKind
            is JsonValueKind.String
                or JsonValueKind.Number
                or JsonValueKind.True
                or JsonValueKind.False;

    private static string ScalarText(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText();
}
