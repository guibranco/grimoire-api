using System.Text;

namespace Grimoire.Core.Import;

internal enum TokenKind
{
    String,
    Number,
    Word,
    Variable,
    Punct,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Line)
{
    public bool Is(string text) => string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Splits PHP source into the tokens <see cref="PhpConfigParser"/> reads. Comments, <c>&lt;?php</c> and <c>?&gt;</c> are dropped.</summary>
internal static class PhpTokenizer
{
    public static List<Token> Tokenize(string src)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '\n')
            {
                line++;
                i++;
            }
            else if (char.IsWhiteSpace(c))
                i++;
            else if (StartsWith(src, i, "<?php"))
                i += 5;
            else if (StartsWith(src, i, "?>"))
                i += 2;
            else if (c == '#' || StartsWith(src, i, "//"))
                i = SkipLine(src, i);
            else if (StartsWith(src, i, "/*"))
                i = SkipBlock(src, i, ref line);
            else if (c is '\'' or '"')
                tokens.Add(new Token(TokenKind.String, ReadString(src, ref i, ref line), line));
            else if (char.IsDigit(c))
                tokens.Add(new Token(TokenKind.Number, ReadWhile(src, ref i, IsNumberChar), line));
            else if (c == '$')
            {
                i++;
                tokens.Add(new Token(TokenKind.Variable, ReadWhile(src, ref i, IsWordChar), line));
            }
            else if (IsWordChar(c))
                tokens.Add(new Token(TokenKind.Word, ReadWhile(src, ref i, IsWordChar), line));
            else if (StartsWith(src, i, "=>"))
            {
                tokens.Add(new Token(TokenKind.Punct, "=>", line));
                i += 2;
            }
            else
            {
                tokens.Add(new Token(TokenKind.Punct, c.ToString(), line));
                i++;
            }
        }
        tokens.Add(new Token(TokenKind.End, "", line));
        return tokens;
    }

    private static bool StartsWith(string s, int i, string value) =>
        string.CompareOrdinal(s, i, value, 0, value.Length) == 0;

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '\\';

    private static bool IsNumberChar(char c) => char.IsLetterOrDigit(c) || c is '.' or '_';

    private static string ReadWhile(string s, ref int i, Func<char, bool> keep)
    {
        var start = i;
        while (i < s.Length && keep(s[i]))
            i++;
        return s[start..i];
    }

    private static int SkipLine(string s, int i)
    {
        var end = s.IndexOf('\n', i);
        return end < 0 ? s.Length : end;
    }

    private static int SkipBlock(string s, int i, ref int line)
    {
        var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
        var stop = end < 0 ? s.Length : end + 2;
        line += s.AsSpan(i, stop - i).Count('\n');
        return stop;
    }

    /// <summary>A quoted string. Single quotes only unescape <c>\'</c> and <c>\\</c>; double quotes also <c>\n</c>, <c>\t</c>, <c>\r</c>, <c>\$</c> and <c>\"</c> (variables are not interpolated).</summary>
    private static string ReadString(string s, ref int i, ref int line)
    {
        var quote = s[i];
        var startLine = line;
        var sb = new StringBuilder();
        i++;
        while (i < s.Length && s[i] != quote)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                var escaped = Unescape(quote, n);
                if (escaped is not null)
                {
                    sb.Append(escaped);
                    i += 2;
                    continue;
                }
            }
            if (c == '\n')
                line++;
            sb.Append(c);
            i++;
        }
        if (i >= s.Length)
            throw new ImportParseException($"Line {startLine}: unterminated string.");
        i++;
        return sb.ToString();
    }

    private static string? Unescape(char quote, char n) =>
        (quote, n) switch
        {
            (_, '\\') => "\\",
            (_, var q) when q == quote => q.ToString(),
            ('"', 'n') => "\n",
            ('"', 't') => "\t",
            ('"', 'r') => "\r",
            ('"', '$') => "$",
            _ => null,
        };
}
