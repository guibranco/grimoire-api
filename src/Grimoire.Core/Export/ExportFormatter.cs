using System.Text;

namespace Grimoire.Core.Export;

public enum ExportFormat
{
    Json,
    Env,
    Dotnet,
}

/// <summary>What an export holds: secret values by name and configuration values by key.</summary>
public sealed record ExportedValues(
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyDictionary<string, string> Configurations
);

/// <summary>
/// Turns exported values into a dotenv file or a flat <c>IConfiguration</c>-style object.
/// When a secret and a configuration entry end up with the same key, the secret wins.
/// </summary>
public static class ExportFormatter
{
    private static readonly (string Name, ExportFormat Format)[] Map =
    [
        ("json", ExportFormat.Json),
        ("env", ExportFormat.Env),
        ("dotnet", ExportFormat.Dotnet),
    ];

    public static IReadOnlyList<string> Formats { get; } = Map.Select(m => m.Name).ToArray();

    /// <summary>Parses a format name, ignoring case. An empty value means <see cref="ExportFormat.Json"/>.</summary>
    public static bool TryParseFormat(string? value, out ExportFormat format)
    {
        format = ExportFormat.Json;
        if (string.IsNullOrEmpty(value))
            return true;
        foreach (var (name, f) in Map)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                format = f;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A dotenv variable name: <c>:</c> becomes <c>__</c> (as .NET reads environment variables),
    /// any other character that isn't a letter, digit or <c>_</c> becomes <c>_</c>, and the result
    /// is upper case. <c>Feature:DarkMode</c> → <c>FEATURE__DARKMODE</c>, <c>db-password</c> → <c>DB_PASSWORD</c>.
    /// </summary>
    public static string EnvKey(string key)
    {
        var sb = new StringBuilder(key.Length + 4);
        foreach (var c in key)
        {
            if (c == ':')
                sb.Append("__");
            else if (char.IsAsciiLetterOrDigit(c) || c == '_')
                sb.Append(char.ToUpperInvariant(c));
            else
                sb.Append('_');
        }
        if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    /// <summary>
    /// A dotenv value. Single quotes (taken literally by dotenv readers) unless the value holds a
    /// single quote or a line break; then double quotes, escaping <c>\</c>, <c>"</c>, <c>$</c> and line breaks.
    /// </summary>
    public static string EnvValue(string value)
    {
        if (value.IndexOfAny(['\'', '\n', '\r']) < 0)
            return $"'{value}'";

        var sb = new StringBuilder(value.Length + 8).Append('"');
        foreach (var c in value)
        {
            sb.Append(
                c switch
                {
                    '\\' => @"\\",
                    '"' => "\\\"",
                    '$' => "\\$",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    _ => c.ToString(),
                }
            );
        }
        return sb.Append('"').ToString();
    }

    /// <summary>One <c>KEY=value</c> line per value, sorted by name.</summary>
    public static string ToEnv(ExportedValues values)
    {
        var lines = Merge(values, EnvKey).Select(kv => $"{kv.Key}={EnvValue(kv.Value)}").ToList();
        return lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";
    }

    /// <summary>A flat object keyed as <c>IConfiguration</c> reads it (<c>Feature:DarkMode</c>), sorted by key.</summary>
    public static SortedDictionary<string, string> ToDotnet(ExportedValues values) =>
        Merge(values, key => key);

    private static SortedDictionary<string, string> Merge(
        ExportedValues values,
        Func<string, string> key
    )
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in values.Configurations)
            merged[key(k)] = v;
        foreach (var (k, v) in values.Secrets)
            merged[key(k)] = v;
        return merged;
    }
}
