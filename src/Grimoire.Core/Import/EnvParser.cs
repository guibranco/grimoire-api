using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.Core.Import;

/// <summary>
/// dotenv files: <c>KEY=value</c> per line, optional <c>export </c>, <c>#</c> comments, single
/// quotes (literal), double quotes (escapes, may span lines) and unquoted values (trimmed, ending
/// at <c> #</c>).
/// </summary>
public static partial class EnvParser
{
    [GeneratedRegex(@"^(?:export\s+)?([A-Za-z_][A-Za-z0-9_.:-]*)\s*=(.*)$")]
    private static partial Regex Assignment();

    public static List<ImportedValue> Parse(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var values = new List<ImportedValue>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var m = Assignment().Match(line);
            if (!m.Success)
                throw new ImportParseException($"Line {i + 1}: expected KEY=value.");
            var key = m.Groups[1].Value;
            var raw = m.Groups[2].Value.TrimStart();
            string value;
            if (raw.StartsWith('"'))
                (value, i) = ReadDoubleQuoted(lines, i, raw);
            else if (raw.StartsWith('\''))
                value = ReadSingleQuoted(raw, i);
            else
                value = Unquoted(raw);
            values.Add(new ImportedValue(key, value, ImportParser.TextType(value)));
        }
        return values;
    }

    private static string Unquoted(string raw)
    {
        var comment = raw.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? raw[..comment] : raw).Trim();
    }

    private static string ReadSingleQuoted(string raw, int line)
    {
        var end = raw.IndexOf('\'', 1);
        if (end < 0)
            throw new ImportParseException($"Line {line + 1}: unterminated single quote.");
        return raw[1..end];
    }

    /// <summary>The value and the index of the line it ends on.</summary>
    private static (string Value, int EndLine) ReadDoubleQuoted(
        string[] lines,
        int line,
        string raw
    )
    {
        var sb = new StringBuilder();
        var text = raw[1..];
        var current = line;
        while (true)
        {
            for (var j = 0; j < text.Length; j++)
            {
                var c = text[j];
                if (c == '"')
                    return (sb.ToString(), current);
                if (c == '\\' && j + 1 < text.Length)
                {
                    sb.Append(Escape(text[++j]));
                    continue;
                }
                sb.Append(c);
            }
            if (++current >= lines.Length)
                throw new ImportParseException($"Line {line + 1}: unterminated double quote.");
            sb.Append('\n');
            text = lines[current];
        }
    }

    private static string Escape(char c) =>
        c switch
        {
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            _ => c.ToString(),
        };
}
