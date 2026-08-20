using System.Globalization;
using System.Text;

namespace Detangle.Core.Linking;

/// <summary>
/// The normalization function <c>N(s)</c> from plan.md section 5.2. One pass over a
/// link target or a filename collapses the four drifts that break naive viewers at
/// once: case, separators (space / hyphen / underscore / dot), percent-encoding, and
/// Unicode composition. Two strings that normalize equal are treated as the same
/// identifier by chain step 6.
/// </summary>
public static class LinkNormalizer
{
    /// <summary>
    /// Extensions stripped before normalizing. Stripping happens before the dot
    /// collapse, otherwise "note.md" would normalize to "note-md" and never match the
    /// file it names.
    /// </summary>
    private static readonly string[] KnownExtensions =
    [
        ".markdown", ".mdown", ".mdx", ".mkd", ".html", ".htm", ".txt", ".md",
    ];

    /// <summary>Applies the full normalization chain.</summary>
    public static string Normalize(string? value) => Normalize(value, stripExtension: true);

    /// <summary>
    /// Applies the normalization chain, optionally keeping the extension. Attachment
    /// probes (chain step 11) need the extension intact, so they pass false.
    /// </summary>
    public static string Normalize(string? value, bool stripExtension)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = value.Normalize(NormalizationForm.FormC);
        text = DecodePercentEscapes(text);
        text = text.Trim().Replace('\\', '/');

        if (stripExtension)
        {
            text = StripKnownExtension(text);
        }

        text = text.ToLowerInvariant();

        var builder = new StringBuilder(text.Length);
        bool pendingSeparator = false;

        foreach (char c in text)
        {
            // Every separator flavour folds to a single hyphen, and runs collapse.
            if (c is '_' or '.' or '-' || char.IsWhiteSpace(c))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append('-');
                pendingSeparator = false;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Normalizes each path segment independently, so separator collapsing never eats
    /// the "/" that carries the folder structure a path-suffix match needs.
    /// </summary>
    public static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = DecodePercentEscapes(value.Normalize(NormalizationForm.FormC));
        text = StripKnownExtension(text.Trim().Replace('\\', '/'));

        string[] segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(segments.Length);

        foreach (string segment in segments)
        {
            // "." and ".." are structure, not names: they survive to the relative-path
            // step untouched. Everything else normalizes.
            if (segment is "." or "..")
            {
                normalized.Add(segment);
                continue;
            }

            string normalizedSegment = Normalize(segment, stripExtension: false);
            if (normalizedSegment.Length > 0)
            {
                normalized.Add(normalizedSegment);
            }
        }

        return string.Join('/', normalized);
    }

    /// <summary>
    /// URL-decodes repeatedly while the string keeps changing, capped at two rounds.
    /// LLM-written wikis double-encode ("%2520") often enough to matter; more rounds
    /// would start corrupting targets that legitimately contain a literal percent.
    /// </summary>
    public static string DecodePercentEscapes(string value)
    {
        string current = value;

        for (int round = 0; round < 2; round++)
        {
            string decoded = DecodeOnce(current);
            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                return current;
            }

            current = decoded;
        }

        return current;
    }

    /// <summary>Strips a trailing extension only when it is one Detangle knows.</summary>
    public static string StripKnownExtension(string value)
    {
        foreach (string extension in KnownExtensions)
        {
            if (value.Length > extension.Length
                && value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return value[..^extension.Length];
            }
        }

        return value;
    }

    /// <summary>True when the target names a file type Detangle renders as a document.</summary>
    public static bool HasMarkdownExtension(string value)
    {
        string extension = Path.GetExtension(value);

        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mdown", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mdx", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeOnce(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
        {
            return value;
        }

        var bytes = new List<byte>(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length
                && IsHex(value[i + 1]) && IsHex(value[i + 2]))
            {
                bytes.Add(byte.Parse(
                    value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
                continue;
            }

            // Non-escape characters pass through as UTF-8 so that a decoded multi-byte
            // sequence and the surrounding text end up in one buffer.
            bytes.AddRange(Encoding.UTF8.GetBytes(value.AsSpan(i, 1).ToString()));
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
}
