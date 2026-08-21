using System.Text;

namespace Detangle.Rendering.Typesetting;

/// <summary>
/// Parses the subset of TeX that wikis actually contain into a layout tree.
/// <para>
/// This exists because the alternative was worse. The plan called for KaTeX, which needs
/// a browser — and the whole claim of this application is that it renders everything in
/// process with no network and no web engine. Diagrams already work that way, so math had
/// to as well, and until now it was being dumped on the page as raw source.
/// </para>
/// <para>
/// It is a subset on purpose: fractions, radicals, scripts, growing delimiters, greek and
/// the usual relations cover essentially all of it. Anything else is preserved as its own
/// source rather than approximated, so a reader is never shown notation that says
/// something the author did not write.
/// </para>
/// </summary>
public static class TexParser
{
    private const int MaxDepth = 32;

    /// <summary>Parses a TeX fragment.</summary>
    /// <param name="source">The source, without its delimiters.</param>
    public static MathNode Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var reader = new Reader(source);
        MathNode node = ParseRow(reader, depth: 0, stopAtRight: false);

        return node;
    }

    private static MathNode ParseRow(Reader reader, int depth, bool stopAtRight)
    {
        var children = new List<MathNode>();

        while (!reader.AtEnd)
        {
            char c = reader.Peek;

            if (c == '}')
            {
                break;
            }

            if (stopAtRight && reader.StartsWith("\\right"))
            {
                break;
            }

            MathNode? node = ParseUnit(reader, depth);

            if (node is null)
            {
                continue;
            }

            // Scripts bind to whatever came immediately before them, so they are applied
            // here rather than inside ParseUnit.
            node = ApplyScripts(reader, node, depth);

            children.Add(node);
        }

        return children.Count == 1 ? children[0] : new MathRow(children);
    }

    private static MathNode ApplyScripts(Reader reader, MathNode nucleus, int depth)
    {
        MathNode? superscript = null;
        MathNode? subscript = null;

        while (!reader.AtEnd && (reader.Peek == '^' || reader.Peek == '_'))
        {
            bool isSuperscript = reader.Peek == '^';
            reader.Advance();

            MathNode script = ParseUnit(reader, depth + 1) ?? new MathRow([]);

            if (isSuperscript)
            {
                superscript = superscript is null ? script : new MathRow([superscript, script]);
            }
            else
            {
                subscript = subscript is null ? script : new MathRow([subscript, script]);
            }
        }

        return superscript is null && subscript is null
            ? nucleus
            : new MathScripts(nucleus, superscript, subscript);
    }

    /// <summary>Parses one atom, group or command. Returns null for whitespace.</summary>
    private static MathNode? ParseUnit(Reader reader, int depth)
    {
        if (depth > MaxDepth)
        {
            return new MathUnknown(reader.TakeRest());
        }

        char c = reader.Peek;

        if (char.IsWhiteSpace(c))
        {
            reader.Advance();
            return null;
        }

        if (c == '{')
        {
            reader.Advance();
            MathNode body = ParseRow(reader, depth + 1, stopAtRight: false);
            reader.Expect('}');

            return body;
        }

        if (c == '\\')
        {
            return ParseCommand(reader, depth);
        }

        reader.Advance();

        // Digits run together so that "128" is one atom rather than three, which matters
        // for spacing and for what a script attaches to.
        if (char.IsAsciiDigit(c))
        {
            var digits = new StringBuilder().Append(c);

            while (!reader.AtEnd && (char.IsAsciiDigit(reader.Peek) || reader.Peek == '.'))
            {
                digits.Append(reader.Peek);
                reader.Advance();
            }

            return new MathAtom(digits.ToString(), MathStyle.Upright);
        }

        return new MathAtom(
            c.ToString(),
            char.IsLetter(c) ? MathStyle.Variable : MathStyle.Upright);
    }

    private static MathNode ParseCommand(Reader reader, int depth)
    {
        int start = reader.Position;
        reader.Advance();

        if (reader.AtEnd)
        {
            return new MathUnknown("\\");
        }

        // A backslash before punctuation is an escape, not a name: "\{" is a brace.
        if (!char.IsLetter(reader.Peek))
        {
            char escaped = reader.Peek;
            reader.Advance();

            if (TexSymbols.Spaces.TryGetValue(escaped.ToString(), out double thin))
            {
                return new MathSpace(thin);
            }

            return new MathAtom(escaped.ToString(), MathStyle.Upright);
        }

        string name = reader.TakeName();

        switch (name)
        {
            case "frac" or "dfrac" or "tfrac":
                return new MathFraction(TakeArgument(reader, depth), TakeArgument(reader, depth));

            case "sqrt":
                MathNode? index = null;

                if (!reader.AtEnd && reader.Peek == '[')
                {
                    // The index is delimited by a bracket, which the row parser does not
                    // stop at - it would swallow the radicand as well.
                    reader.Advance();
                    index = Parse(reader.TakeUntil(']'));
                    reader.Expect(']');
                }

                return new MathRadical(TakeArgument(reader, depth), index);

            case "text" or "mathrm" or "textrm" or "operatorname" or "mathsf" or "mathtt":
                return new MathAtom(TakeLiteral(reader), MathStyle.Text);

            case "mathbf" or "textbf" or "bm" or "boldsymbol":
                return new MathAtom(TakeLiteral(reader), MathStyle.Text);

            case "mathit" or "textit":
                return new MathAtom(TakeLiteral(reader), MathStyle.Variable);

            case "left":
                return ParseFenced(reader, depth);

            case "right":
                // A \right with no \left: keep the delimiter rather than losing it.
                return new MathAtom(TakeDelimiter(reader), MathStyle.Upright);

            default:
                if (TexSymbols.Spaces.TryGetValue(name, out double space))
                {
                    return new MathSpace(space);
                }

                if (TexSymbols.Map.TryGetValue(name, out (string Text, MathStyle Style) symbol))
                {
                    return new MathAtom(symbol.Text, symbol.Style);
                }

                return new MathUnknown(reader.Slice(start, reader.Position));
        }
    }

    private static MathNode ParseFenced(Reader reader, int depth)
    {
        string open = TakeDelimiter(reader);
        MathNode body = ParseRow(reader, depth + 1, stopAtRight: true);
        string close = string.Empty;

        if (reader.StartsWith("\\right"))
        {
            reader.Skip("\\right".Length);
            close = TakeDelimiter(reader);
        }

        return new MathFenced(open, close, body);
    }

    private static string TakeDelimiter(Reader reader)
    {
        reader.SkipWhitespace();

        if (reader.AtEnd)
        {
            return string.Empty;
        }

        if (reader.Peek == '\\')
        {
            int start = reader.Position;
            reader.Advance();

            string name = char.IsLetter(reader.Peek) ? reader.TakeName() : TakeOne(reader);
            string token = "\\" + name;

            if (TexSymbols.Delimiters.TryGetValue(token, out string? mapped))
            {
                return mapped;
            }

            reader.Rewind(start);
            reader.Advance();

            return string.Empty;
        }

        string single = TakeOne(reader);

        return TexSymbols.Delimiters.TryGetValue(single, out string? delimiter) ? delimiter : single;
    }

    private static string TakeOne(Reader reader)
    {
        char c = reader.Peek;
        reader.Advance();

        return c.ToString();
    }

    /// <summary>
    /// Reads one argument: a braced group, a command, or exactly one character.
    /// <para>
    /// The single-character rule is what makes "rac12" one half rather than twelve over
    /// nothing. An argument is one token in TeX, and a run of digits is only a number when
    /// it is not standing in for a brace group.
    /// </para>
    /// </summary>
    private static MathNode TakeArgument(Reader reader, int depth)
    {
        reader.SkipWhitespace();

        if (reader.AtEnd)
        {
            return new MathRow([]);
        }

        if (reader.Peek is '{' or '\\')
        {
            return ParseUnit(reader, depth + 1) ?? new MathRow([]);
        }

        char c = reader.Peek;
        reader.Advance();

        return new MathAtom(
            c.ToString(),
            char.IsLetter(c) ? MathStyle.Variable : MathStyle.Upright);
    }

    /// <summary>
    /// Reads the contents of <c>\text{…}</c> as literal characters. Everything inside is
    /// words, so parsing it as math would italicise it and eat the spaces.
    /// </summary>
    private static string TakeLiteral(Reader reader)
    {
        reader.SkipWhitespace();

        if (reader.AtEnd || reader.Peek != '{')
        {
            return reader.AtEnd ? string.Empty : TakeOne(reader);
        }

        reader.Advance();

        var text = new StringBuilder();
        int nesting = 1;

        while (!reader.AtEnd)
        {
            char c = reader.Peek;

            if (c == '{')
            {
                nesting++;
            }
            else if (c == '}' && --nesting == 0)
            {
                reader.Advance();
                break;
            }

            text.Append(c);
            reader.Advance();
        }

        return text.ToString();
    }

    /// <summary>A cursor over the source.</summary>
    private sealed class Reader(string source)
    {
        public int Position { get; private set; }

        public bool AtEnd => Position >= source.Length;

        public char Peek => Position < source.Length ? source[Position] : '\0';

        public void Advance() => Position++;

        public void Skip(int count) => Position += count;

        public void Rewind(int position) => Position = position;

        public bool StartsWith(string value) =>
            string.CompareOrdinal(source, Position, value, 0, value.Length) == 0;

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Peek))
            {
                Position++;
            }
        }

        public void Expect(char c)
        {
            if (!AtEnd && Peek == c)
            {
                Position++;
            }
        }

        public string TakeName()
        {
            int start = Position;

            while (!AtEnd && char.IsLetter(Peek))
            {
                Position++;
            }

            return source[start..Position];
        }

        public string Slice(int start, int end) => source[start..end];

        /// <summary>Reads up to a closing character, without consuming it.</summary>
        public string TakeUntil(char stop)
        {
            int start = Position;

            while (!AtEnd && Peek != stop)
            {
                Position++;
            }

            return source[start..Position];
        }

        public string TakeRest()
        {
            string rest = source[Position..];
            Position = source.Length;

            return rest;
        }
    }
}
