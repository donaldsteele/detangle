using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Resolves CSS custom properties and <c>color-mix()</c> in an SVG into literal values.
/// <para>
/// Mermaider writes SVG for a browser: colours and font sizes come out as
/// <c>var(--fg)</c> and <c>var(--fs-m)</c>, defined in a style block that itself refers to
/// variables the host page is expected to supply. A browser resolves all of that. The SVG
/// renderer this application draws with implements none of it, so an unresolved
/// <c>font-size</c> collapsed every label in every diagram to nothing — the shapes drew,
/// the words did not.
/// </para>
/// <para>
/// Flattening happens before the picture is handed to the renderer, which means the cached
/// SVG on disk is already literal and the fix costs nothing per frame.
/// </para>
/// </summary>
internal static partial class SvgStyleFlattener
{
    private const int MaxPasses = 8;

    /// <summary>Rewrites an SVG so that no <c>var()</c> or <c>color-mix()</c> remains.</summary>
    /// <param name="svg">The SVG source.</param>
    /// <param name="seed">Root variables the host page would otherwise supply.</param>
    public static string Flatten(string svg, IReadOnlyDictionary<string, string> seed)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(seed);

        if (!svg.Contains("var(", StringComparison.Ordinal)
            && !svg.Contains("color-mix(", StringComparison.Ordinal))
        {
            return svg;
        }

        Dictionary<string, string> variables = Collect(svg, seed);


        // Definitions refer to each other, so they are resolved among themselves first;
        // the document then only needs one substitution pass.
        foreach (string name in variables.Keys.ToList())
        {
            variables[name] = Resolve(variables[name], variables, MaxPasses);
        }

        return NormaliseFontFamilies(Resolve(svg, variables, MaxPasses));
    }

    /// <summary>
    /// Reduces the named font stacks to a generic family.
    /// <para>
    /// Mermaider asks for Inter and Segoe UI, neither of which a platform font manager is
    /// obliged to have. A generic is one every renderer answers.
    /// </para>
    /// </summary>
    private static string NormaliseFontFamilies(string svg) =>
        FontFamilyDeclaration().Replace(svg, match =>
            match.Value.Contains("mono", StringComparison.OrdinalIgnoreCase)
                ? "font-family: monospace"
                : "font-family: sans-serif");

    /// <summary>
    /// Removes every font-family declaration, leaving the renderer to pick a face.
    /// <para>
    /// For platforms that draw text correctly until a family arrives through CSS, and then
    /// stack every glyph of a word at one position. Text with no family set draws properly
    /// there, so dropping the declaration is the whole remedy — the labels stay real text,
    /// selectable and searchable, rather than being outlined into paths.
    /// </para>
    /// </summary>
    public static string RemoveFontFamilies(string svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        return FontFamilyDeclaration().Replace(svg, string.Empty);
    }

    /// <summary>
    /// Gathers every custom property the document declares, then lets the seed win. The
    /// seed carries the palette this application chose; the document's own values for
    /// those names are the defaults it would have used without a host.
    /// </summary>
    private static Dictionary<string, string> Collect(string svg, IReadOnlyDictionary<string, string> seed)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Declaration().Matches(svg))
        {
            variables[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        foreach ((string name, string value) in seed)
        {
            variables[name] = value;
        }

        return variables;
    }

    private static string Resolve(string text, Dictionary<string, string> variables, int passes)
    {
        string current = text;

        for (int pass = 0; pass < passes; pass++)
        {
            string expanded = ExpandVariables(current, variables);
            expanded = ExpandColorMix(expanded);

            if (string.Equals(expanded, current, StringComparison.Ordinal))
            {
                return expanded;
            }

            current = expanded;
        }

        return current;
    }

    /// <summary>
    /// Replaces <c>var(--name)</c> and <c>var(--name, fallback)</c>. Written by hand
    /// rather than with a pattern because the fallback can itself contain a bracketed
    /// function, which no regular expression matches correctly.
    /// </summary>
    private static string ExpandVariables(string text, Dictionary<string, string> variables)
    {
        int start = text.IndexOf("var(", StringComparison.Ordinal);

        if (start < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int position = 0;

        while (start >= 0)
        {
            int end = MatchingBracket(text, start + 3);

            if (end < 0)
            {
                break;
            }

            builder.Append(text, position, start - position);

            string inner = text[(start + 4)..end];
            int comma = TopLevelComma(inner);
            string name = (comma < 0 ? inner : inner[..comma]).Trim();
            string fallback = comma < 0 ? string.Empty : inner[(comma + 1)..].Trim();

            builder.Append(
                variables.TryGetValue(name, out string? value) && value.Length > 0 ? value : fallback);

            position = end + 1;
            start = text.IndexOf("var(", position, StringComparison.Ordinal);
        }

        builder.Append(text, position, text.Length - position);

        return builder.ToString();
    }

    /// <summary>
    /// Evaluates <c>color-mix(in srgb, A p%, B)</c>. Only the sRGB form is implemented,
    /// which is the only one Mermaider emits.
    /// </summary>
    private static string ExpandColorMix(string text)
    {
        int start = text.IndexOf("color-mix(", StringComparison.Ordinal);

        if (start < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int position = 0;

        while (start >= 0)
        {
            int end = MatchingBracket(text, start + 9);

            if (end < 0)
            {
                break;
            }

            builder.Append(text, position, start - position);

            string arguments = text[(start + 10)..end];

            // A mix whose arguments still hold a variable is left for the next pass:
            // evaluating it now would read "var(--fg)" as a colour and fail to a grey.
            builder.Append(
                arguments.Contains("var(", StringComparison.Ordinal)
                    ? text[start..(end + 1)]
                    : Mix(arguments));

            position = end + 1;
            start = text.IndexOf("color-mix(", position, StringComparison.Ordinal);
        }

        builder.Append(text, position, text.Length - position);

        return builder.ToString();
    }

    private static string Mix(string arguments)
    {
        List<string> parts = Split(arguments);

        // "in srgb" plus two colours. Anything else is left to the caller's fallback.
        if (parts.Count < 3)
        {
            return "#808080";
        }

        (string first, double weight) = Weighted(parts[1]);
        (string second, _) = Weighted(parts[2]);

        if (!TryParseColour(first, out (int R, int G, int B) a)
            || !TryParseColour(second, out (int R, int G, int B) b))
        {
            return TryParseColour(first, out (int R, int G, int B) only) ? Hex(only) : "#808080";
        }

        double t = Math.Clamp(weight, 0, 1);

        return Hex((
            (int)Math.Round((a.R * t) + (b.R * (1 - t))),
            (int)Math.Round((a.G * t) + (b.G * (1 - t))),
            (int)Math.Round((a.B * t) + (b.B * (1 - t)))));
    }

    private static (string Colour, double Weight) Weighted(string part)
    {
        string[] pieces = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (pieces.Length >= 2 && pieces[^1].EndsWith('%')
            && double.TryParse(
                pieces[^1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
        {
            return (string.Join(' ', pieces[..^1]), percent / 100);
        }

        return (part.Trim(), 0.5);
    }

    private static bool TryParseColour(string value, out (int R, int G, int B) colour)
    {
        colour = default;
        string text = value.Trim();

        if (text.StartsWith('#'))
        {
            string hex = text[1..];

            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            }

            if (hex.Length >= 6
                && int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
                && int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
                && int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
            {
                colour = (r, g, b);

                return true;
            }
        }

        return text.Equals("white", StringComparison.OrdinalIgnoreCase)
            ? Assign(out colour, (255, 255, 255))
            : text.Equals("black", StringComparison.OrdinalIgnoreCase) && Assign(out colour, (0, 0, 0));
    }

    private static bool Assign(out (int R, int G, int B) target, (int R, int G, int B) value)
    {
        target = value;

        return true;
    }

    private static string Hex((int R, int G, int B) colour) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"#{Math.Clamp(colour.R, 0, 255):x2}{Math.Clamp(colour.G, 0, 255):x2}{Math.Clamp(colour.B, 0, 255):x2}");

    /// <summary>Splits on commas that are not inside brackets.</summary>
    private static List<string> Split(string text)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (text[i] == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);

        return parts;
    }

    private static int TopLevelComma(string text)
    {
        int depth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (text[i] == ',' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index of the bracket closing the one at <paramref name="open"/>.</summary>
    private static int MatchingBracket(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"(--[a-zA-Z0-9_-]+)\s*:\s*([^;{}]+)\s*;")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"font-family\s*:\s*[^;}]+")]
    private static partial Regex FontFamilyDeclaration();
}
