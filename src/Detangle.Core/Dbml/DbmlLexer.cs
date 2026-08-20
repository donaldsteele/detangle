using System.Text;

namespace Detangle.Core.Dbml;

/// <summary>The kinds of token DBML is made of.</summary>
public enum DbmlTokenKind
{
    /// <summary>End of input.</summary>
    End,

    /// <summary>A bare word: a keyword, name, or type.</summary>
    Identifier,

    /// <summary>A quoted string, single or double, including the triple-quoted form.</summary>
    String,

    /// <summary>A backtick expression, which DBML uses for raw SQL defaults.</summary>
    Expression,

    /// <summary>A number.</summary>
    Number,

    /// <summary>"{".</summary>
    OpenBrace,

    /// <summary>"}".</summary>
    CloseBrace,

    /// <summary>"[".</summary>
    OpenBracket,

    /// <summary>"]".</summary>
    CloseBracket,

    /// <summary>"(".</summary>
    OpenParen,

    /// <summary>")".</summary>
    CloseParen,

    /// <summary>",".</summary>
    Comma,

    /// <summary>":".</summary>
    Colon,

    /// <summary>".".</summary>
    Dot,

    /// <summary>"&lt;", "&gt;", "-" or "&lt;&gt;" — a relationship cardinality.</summary>
    Cardinality,
}

/// <summary>One lexed token.</summary>
/// <param name="Kind">What kind of token it is.</param>
/// <param name="Text">Its text, with quotes already stripped for strings.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
public readonly record struct DbmlToken(DbmlTokenKind Kind, string Text, int Line, int Column)
{
    /// <summary>True when this is a bare word equal to <paramref name="keyword"/>, ignoring case.</summary>
    public bool Is(string keyword) =>
        Kind == DbmlTokenKind.Identifier && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => $"{Kind} \"{Text}\" at {Line}:{Column}";
}

/// <summary>
/// Turns DBML source into tokens.
/// <para>
/// Written rather than taken from a package: <c>Ivy.Dbml.Parser</c> exists and is MIT,
/// but at ~1.5K downloads and 17 commits it is a bus-factor dependency under a core
/// feature, and several constructs Detangle needs — enums, table groups, sticky notes —
/// are unverified in it (plan.md section 4.2). The grammar is small enough to own.
/// </para>
/// </summary>
public static class DbmlLexer
{
    /// <summary>Lexes a document. Unterminated strings end the token rather than the lex.</summary>
    public static IReadOnlyList<DbmlToken> Tokenize(string source)
    {
        var tokens = new List<DbmlToken>();

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        int index = 0;
        int line = 1;
        int lineStart = 0;

        int Column() => index - lineStart + 1;

        while (index < text.Length)
        {
            char current = text[index];

            if (current == '\n')
            {
                index++;
                line++;
                lineStart = index;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            // Comments: "//" to end of line, and "/* … */" which may span lines.
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index += 2;

                while (index < text.Length && !(text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/'))
                {
                    if (text[index] == '\n')
                    {
                        line++;
                        lineStart = index + 1;
                    }

                    index++;
                }

                index = Math.Min(index + 2, text.Length);
                continue;
            }

            int startColumn = Column();

            switch (current)
            {
                case '{':
                    tokens.Add(new DbmlToken(DbmlTokenKind.OpenBrace, "{", line, startColumn));
                    index++;
                    continue;
                case '}':
                    tokens.Add(new DbmlToken(DbmlTokenKind.CloseBrace, "}", line, startColumn));
                    index++;
                    continue;
                case '[':
                    tokens.Add(new DbmlToken(DbmlTokenKind.OpenBracket, "[", line, startColumn));
                    index++;
                    continue;
                case ']':
                    tokens.Add(new DbmlToken(DbmlTokenKind.CloseBracket, "]", line, startColumn));
                    index++;
                    continue;
                case '(':
                    tokens.Add(new DbmlToken(DbmlTokenKind.OpenParen, "(", line, startColumn));
                    index++;
                    continue;
                case ')':
                    tokens.Add(new DbmlToken(DbmlTokenKind.CloseParen, ")", line, startColumn));
                    index++;
                    continue;
                case ',':
                    tokens.Add(new DbmlToken(DbmlTokenKind.Comma, ",", line, startColumn));
                    index++;
                    continue;
                case ':':
                    tokens.Add(new DbmlToken(DbmlTokenKind.Colon, ":", line, startColumn));
                    index++;
                    continue;
                case '.':
                    tokens.Add(new DbmlToken(DbmlTokenKind.Dot, ".", line, startColumn));
                    index++;
                    continue;
            }

            if (current is '<' or '>' or '-')
            {
                // "<>" is one token, not two: many-to-many would otherwise lex as
                // one-to-many followed by a stray cardinality.
                if (current == '<' && index + 1 < text.Length && text[index + 1] == '>')
                {
                    tokens.Add(new DbmlToken(DbmlTokenKind.Cardinality, "<>", line, startColumn));
                    index += 2;
                    continue;
                }

                tokens.Add(new DbmlToken(DbmlTokenKind.Cardinality, current.ToString(), line, startColumn));
                index++;
                continue;
            }

            if (current is '\'' or '"')
            {
                tokens.Add(ReadString(text, ref index, ref line, ref lineStart, startColumn, current));
                continue;
            }

            if (current == '`')
            {
                index++;
                var expression = new StringBuilder();

                while (index < text.Length && text[index] != '`')
                {
                    expression.Append(text[index]);
                    index++;
                }

                index = Math.Min(index + 1, text.Length);
                tokens.Add(new DbmlToken(DbmlTokenKind.Expression, expression.ToString(), line, startColumn));
                continue;
            }

            if (char.IsDigit(current))
            {
                int numberStart = index;

                while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
                {
                    index++;
                }

                tokens.Add(new DbmlToken(DbmlTokenKind.Number, text[numberStart..index], line, startColumn));
                continue;
            }

            if (IsIdentifierStart(current))
            {
                int wordStart = index;

                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }

                tokens.Add(new DbmlToken(DbmlTokenKind.Identifier, text[wordStart..index], line, startColumn));
                continue;
            }

            // Anything else is punctuation DBML does not use; skipping keeps one stray
            // character from derailing the rest of the document.
            index++;
        }

        tokens.Add(new DbmlToken(DbmlTokenKind.End, string.Empty, line, Column()));

        return tokens;
    }

    private static DbmlToken ReadString(
        string text, ref int index, ref int line, ref int lineStart, int startColumn, char quote)
    {
        int startLine = line;

        // DBML's triple-quoted form carries multi-line notes.
        bool isTriple = index + 2 < text.Length && text[index + 1] == quote && text[index + 2] == quote;

        index += isTriple ? 3 : 1;

        var value = new StringBuilder();

        while (index < text.Length)
        {
            char current = text[index];

            if (current == '\\' && index + 1 < text.Length)
            {
                value.Append(text[index + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    char escaped => escaped,
                });

                index += 2;
                continue;
            }

            if (current == quote)
            {
                if (!isTriple)
                {
                    index++;
                    break;
                }

                if (index + 2 < text.Length && text[index + 1] == quote && text[index + 2] == quote)
                {
                    index += 3;
                    break;
                }
            }

            if (current == '\n')
            {
                line++;
                lineStart = index + 1;
            }

            value.Append(current);
            index++;
        }

        string content = isTriple ? Dedent(value.ToString()) : value.ToString();

        return new DbmlToken(DbmlTokenKind.String, content, startLine, startColumn);
    }

    /// <summary>
    /// Removes the common leading whitespace from a triple-quoted note, so an indented
    /// block in the source does not render as an indented block of text.
    /// </summary>
    private static string Dedent(string value)
    {
        string[] lines = value.Trim('\n').Split('\n');

        int indent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '#' or '@';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '#' or '@';
}
