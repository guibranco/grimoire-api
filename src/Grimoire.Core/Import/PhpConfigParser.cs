using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Entities;

namespace Grimoire.Core.Import;

/// <summary>
/// PHP config files, read as data and never executed. Understands <c>return [ … ];</c> and
/// <c>return array( … );</c>, <c>define('KEY', value);</c> and <c>$config['a']['b'] = value;</c>.
/// Values may be strings (with <c>.</c> concatenation), numbers, booleans, <c>null</c> and
/// arrays; nested keyed arrays become <c>a:b</c> keys. A value built from anything else
/// (function calls, constants, variables) is skipped and reported.
/// </summary>
public sealed class PhpConfigParser
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly List<Token> _tokens;
    private int _pos;
    private readonly List<ImportedValue> _values = [];
    private readonly List<string> _skipped = [];

    private PhpConfigParser(string content) => _tokens = PhpTokenizer.Tokenize(content);

    /// <summary>The values, plus the keys skipped because their value isn't plain data.</summary>
    public static (List<ImportedValue> Values, List<string> Skipped) Parse(string content)
    {
        var parser = new PhpConfigParser(content);
        parser.ParseStatements();
        return (parser._values, parser._skipped);
    }

    private Token Peek => _tokens[_pos];

    private Token Next() => _tokens[_pos++];

    private bool Accept(TokenKind kind, string? text = null)
    {
        if (Peek.Kind != kind || (text is not null && !Peek.Is(text)))
            return false;
        _pos++;
        return true;
    }

    private Token Expect(TokenKind kind, string? text = null)
    {
        if (Peek.Kind == kind && (text is null || Peek.Is(text)))
            return Next();
        throw new ImportParseException($"Line {Peek.Line}: expected {text ?? kind.ToString()}.");
    }

    private void ParseStatements()
    {
        while (Peek.Kind != TokenKind.End)
        {
            if (Accept(TokenKind.Word, "return"))
            {
                AddValue(null, ParseValue());
                Accept(TokenKind.Punct, ";");
            }
            else if (Peek.Kind == TokenKind.Word && Peek.Is("define"))
                ParseDefine();
            else if (Peek.Kind == TokenKind.Variable)
                ParseAssignment();
            else
                SkipStatement();
        }
    }

    private void ParseDefine()
    {
        Next();
        Expect(TokenKind.Punct, "(");
        var name = Expect(TokenKind.String).Text;
        Expect(TokenKind.Punct, ",");
        var value = ParseValue();
        Expect(TokenKind.Punct, ")");
        Accept(TokenKind.Punct, ";");
        AddValue(name, value);
    }

    /// <summary><c>$config['a']['b'] = value;</c> is key <c>a:b</c>; <c>$x = [ … ];</c> adds the array's keys.</summary>
    private void ParseAssignment()
    {
        Next();
        var path = new List<string>();
        while (Accept(TokenKind.Punct, "["))
        {
            path.Add(Expect(TokenKind.String).Text);
            Expect(TokenKind.Punct, "]");
        }
        if (!Accept(TokenKind.Punct, "="))
        {
            SkipStatement();
            return;
        }
        var value = ParseValue();
        Accept(TokenKind.Punct, ";");
        AddValue(path.Count == 0 ? null : string.Join(':', path), value);
    }

    private void SkipStatement()
    {
        var depth = 0;
        while (Peek.Kind != TokenKind.End)
        {
            var t = Next();
            if (t.Kind != TokenKind.Punct)
                continue;
            if (t.Text is "(" or "[" or "{")
                depth++;
            else if (t.Text is ")" or "]" or "}")
                depth--;
            else if (t.Text == ";" && depth <= 0)
                return;
        }
    }

    private PhpValue ParseValue()
    {
        var first = ParseTerm();
        if (Peek.Kind != TokenKind.Punct || !Peek.Is("."))
            return first;
        var sb = new StringBuilder();
        var ok = first is PhpScalar;
        if (first is PhpScalar s)
            sb.Append(s.Text);
        while (Accept(TokenKind.Punct, "."))
        {
            if (ParseTerm() is PhpScalar next)
                sb.Append(next.Text);
            else
                ok = false;
        }
        return ok ? new PhpScalar(sb.ToString(), true) : new PhpUnsupported();
    }

    private PhpValue ParseTerm()
    {
        var t = Peek;
        switch (t.Kind)
        {
            case TokenKind.String:
                Next();
                return new PhpScalar(t.Text, true);
            case TokenKind.Number:
                Next();
                return new PhpScalar(t.Text, false);
            case TokenKind.Punct when t.Is("["):
                Next();
                return ParseArrayItems("]");
            case TokenKind.Punct when t.Is("-") && _tokens[_pos + 1].Kind == TokenKind.Number:
                Next();
                return new PhpScalar("-" + Next().Text, false);
            case TokenKind.Word when t.Is("array") && _tokens[_pos + 1].Is("("):
                Next();
                Next();
                return ParseArrayItems(")");
            case TokenKind.Word when t.Is("true") || t.Is("false"):
                Next();
                return new PhpScalar(t.Text.ToLowerInvariant(), false);
            case TokenKind.Word when t.Is("null"):
                Next();
                return new PhpScalar("", false);
            default:
                SkipExpression();
                return new PhpUnsupported();
        }
    }

    /// <summary>Skips an expression we don't read (a call, constant or variable) up to the next <c>,</c>, <c>;</c> or closing bracket.</summary>
    private void SkipExpression()
    {
        var depth = 0;
        while (Peek.Kind != TokenKind.End)
        {
            var t = Peek;
            if (t.Kind == TokenKind.Punct)
            {
                if (depth == 0 && (t.Text is "," or ";" or ")" or "]" || t.Text == "=>"))
                    return;
                if (t.Text is "(" or "[")
                    depth++;
                else if (t.Text is ")" or "]")
                    depth--;
            }
            Next();
        }
    }

    private PhpArray ParseArrayItems(string close)
    {
        var items = new List<(string? Key, PhpValue Value)>();
        while (!Accept(TokenKind.Punct, close))
        {
            var value = ParseValue();
            string? key = null;
            if (Accept(TokenKind.Punct, "=>"))
            {
                key = value is PhpScalar k ? k.Text : null;
                value = ParseValue();
            }
            items.Add((key, value));
            if (!Accept(TokenKind.Punct, ","))
            {
                Expect(TokenKind.Punct, close);
                break;
            }
        }
        return new PhpArray(items);
    }

    private void AddValue(string? key, PhpValue value)
    {
        switch (value)
        {
            case PhpArray array when key is null || array.IsKeyed:
                var index = 0;
                foreach (var (itemKey, item) in array.Items)
                {
                    var name = itemKey ?? (index++).ToString(CultureInfo.InvariantCulture);
                    AddValue(key is null ? name : $"{key}:{name}", item);
                }
                break;
            case PhpArray list when list.IsPlainData:
                _values.Add(ToImported(key, list));
                break;
            case PhpScalar scalar when key is not null:
                _values.Add(
                    new ImportedValue(key, scalar.Text, ImportParser.TextType(scalar.Text))
                );
                break;
            default:
                if (key is not null)
                    _skipped.Add(key);
                break;
        }
    }

    private static ImportedValue ToImported(string key, PhpArray list) =>
        list.Items.All(i => i.Value is PhpScalar)
            ? new ImportedValue(
                key,
                JsonSerializer.Serialize(
                    list.Items.Select(i => ((PhpScalar)i.Value).Text),
                    Compact
                ),
                ConfigurationValueType.Array
            )
            : new ImportedValue(
                key,
                list.ToJson()!.ToJsonString(Compact),
                ConfigurationValueType.Json
            );

    private abstract record PhpValue
    {
        /// <summary>Whether the value is made only of literals (no calls, constants or variables).</summary>
        public virtual bool IsPlainData => true;

        public abstract JsonNode? ToJson();
    }

    private sealed record PhpScalar(string Text, bool IsString) : PhpValue
    {
        public override JsonNode? ToJson()
        {
            if (IsString)
                return JsonValue.Create(Text);
            if (Text is "true" or "false")
                return JsonValue.Create(Text == "true");
            if (Text.Length == 0)
                return null;
            return decimal.TryParse(
                Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var n
            )
                ? JsonValue.Create(n)
                : JsonValue.Create(Text);
        }
    }

    private sealed record PhpUnsupported : PhpValue
    {
        public override bool IsPlainData => false;

        public override JsonNode? ToJson() => null;
    }

    private sealed record PhpArray(List<(string? Key, PhpValue Value)> Items) : PhpValue
    {
        public bool IsKeyed => Items.Any(i => i.Key is not null);

        public override bool IsPlainData => Items.All(i => i.Value.IsPlainData);

        public override JsonNode? ToJson()
        {
            if (!IsKeyed)
                return new JsonArray(Items.Select(i => i.Value.ToJson()).ToArray());
            var obj = new JsonObject();
            foreach (var (key, value) in Items)
                obj[key ?? ""] = value.ToJson();
            return obj;
        }
    }
}
