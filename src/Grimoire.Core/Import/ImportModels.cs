using Grimoire.Core.Entities;

namespace Grimoire.Core.Import;

/// <summary>The file formats <c>POST …/import</c> reads.</summary>
public enum ImportFormat
{
    /// <summary>dotenv: <c>KEY=value</c> lines.</summary>
    Env,

    /// <summary>appsettings.json: nested objects flattened to <c>Section:Key</c>.</summary>
    AppSettings,

    /// <summary>A PHP config file: <c>return [ … ];</c> or <c>define('KEY', 'value');</c>, read as data.</summary>
    Php,

    /// <summary>A flat JSON object of key → value.</summary>
    Json,
}

/// <summary>A key and value read from an imported file.</summary>
/// <param name="Key">The key, with nested sections joined by <c>:</c>.</param>
/// <param name="Value">The value as text; arrays and objects as JSON.</param>
/// <param name="Type">How the value is shaped, for configuration entries.</param>
public sealed record ImportedValue(string Key, string Value, ConfigurationValueType Type);

/// <summary>What a file held.</summary>
/// <param name="Values">The keys and values, in file order, each key once (its last value).</param>
/// <param name="Skipped">Keys whose value isn't plain data (e.g. a PHP function call), so weren't read.</param>
public sealed record ImportParseResult(
    IReadOnlyList<ImportedValue> Values,
    IReadOnlyList<string> Skipped
);

/// <summary>The file could not be read; <see cref="Exception.Message"/> says where and why.</summary>
public sealed class ImportParseException(string message) : Exception(message);

/// <summary>Reads an imported file into keys and values, without running or evaluating it.</summary>
public static class ImportParser
{
    public static readonly IReadOnlyDictionary<string, ImportFormat> Formats = new Dictionary<
        string,
        ImportFormat
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["env"] = ImportFormat.Env,
        ["appsettings"] = ImportFormat.AppSettings,
        ["php"] = ImportFormat.Php,
        ["json"] = ImportFormat.Json,
    };

    public static bool TryParseFormat(string? name, out ImportFormat format) =>
        Formats.TryGetValue(name ?? string.Empty, out format);

    /// <summary>
    /// The values in <paramref name="content"/>, in file order. A key that appears more than once
    /// keeps its last value, as dotenv and PHP arrays do.
    /// </summary>
    /// <exception cref="ImportParseException">The content isn't valid for the format.</exception>
    public static ImportParseResult Parse(ImportFormat format, string content)
    {
        var (values, skipped) = format switch
        {
            ImportFormat.Env => (EnvParser.Parse(content), []),
            ImportFormat.AppSettings => (JsonImportParser.Parse(content, flatten: true), []),
            ImportFormat.Json => (JsonImportParser.Parse(content, flatten: false), []),
            ImportFormat.Php => PhpConfigParser.Parse(content),
            _ => throw new ImportParseException("Unknown format."),
        };
        var lastIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < values.Count; i++)
            lastIndex[values[i].Key] = i;
        return new ImportParseResult(
            values.Where((v, i) => lastIndex[v.Key] == i).ToList(),
            skipped.Distinct().ToList()
        );
    }

    /// <summary>A text value's type: multiline when it has line breaks.</summary>
    internal static ConfigurationValueType TextType(string value) =>
        value.Contains('\n') ? ConfigurationValueType.Multiline : ConfigurationValueType.String;
}
