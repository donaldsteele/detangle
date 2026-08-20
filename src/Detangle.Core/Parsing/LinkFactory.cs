using System.Text.RegularExpressions;
using Detangle.Core.Linking;

namespace Detangle.Core.Parsing;

/// <summary>
/// Turns raw link syntax into a <see cref="LinkReference"/>. Extraction (which builds
/// the graph) and rendering (which draws the links) both need exactly these rules —
/// the pipe overload, the "#^" ordering, embed detection — and two copies of them would
/// drift, so they live here once.
/// </summary>
public static partial class LinkFactory
{
    /// <summary>Builds a reference from the inside of a "[[…]]".</summary>
    /// <param name="sourcePath">Vault-relative path of the linking document.</param>
    /// <param name="body">The text between the brackets.</param>
    /// <param name="isEmbed">True when the link was written as "![[…]]".</param>
    /// <param name="line">1-based line of the link.</param>
    /// <param name="column">0-based column of the link.</param>
    /// <returns>The reference, or null when the syntax addresses nothing.</returns>
    public static LinkReference? FromWikiLink(
        string sourcePath, string body, bool isEmbed, int line, int column)
    {
        if (body.Contains("[[", StringComparison.Ordinal))
        {
            return null;
        }

        string left = body;
        string? right = null;

        int pipe = body.IndexOf('|', StringComparison.Ordinal);
        if (pipe >= 0)
        {
            left = body[..pipe];
            right = body[(pipe + 1)..].Trim();
        }

        (string target, string? anchor, bool isBlockId) = SplitAnchor(left.Trim());

        if (target.Length == 0 && anchor is null)
        {
            // "[[|alias]]" and "[[]]" address nothing at all.
            return null;
        }

        // The "|" overload (plan.md section 3.2): in an embed whose target is not a
        // document, the pipe payload is a display size rather than an alias.
        bool isSize = isEmbed
            && right is { Length: > 0 }
            && SizeSpecPattern().IsMatch(right)
            && !LinkNormalizer.HasMarkdownExtension(target);

        return new LinkReference
        {
            SourcePath = sourcePath,
            RawTarget = target,
            Label = isSize ? null : right is { Length: > 0 } ? right : null,
            SizeSpec = isSize ? right : null,
            Anchor = anchor,
            AnchorIsBlockId = isBlockId,
            IsEmbed = isEmbed,
            Syntax = LinkSyntax.WikiLink,
            Line = line,
            Column = column,
        };
    }

    /// <summary>Builds a reference from a markdown link or image.</summary>
    /// <param name="sourcePath">Vault-relative path of the linking document.</param>
    /// <param name="url">The link destination as written.</param>
    /// <param name="label">The link text, if any.</param>
    /// <param name="isImage">True for "![alt](src)".</param>
    /// <param name="line">1-based line of the link.</param>
    /// <param name="column">0-based column of the link.</param>
    /// <returns>The reference, or null when the destination is empty.</returns>
    public static LinkReference? FromMarkdownLink(
        string sourcePath, string url, string? label, bool isImage, int line, int column)
    {
        if (url.Length == 0)
        {
            return null;
        }

        (string target, string? anchor, bool isBlockId) = SplitAnchor(url);

        return new LinkReference
        {
            SourcePath = sourcePath,
            RawTarget = target,
            Label = string.IsNullOrWhiteSpace(label) ? null : label,
            Anchor = anchor,
            AnchorIsBlockId = isBlockId,
            IsEmbed = isImage,
            Syntax = LinkSyntax.Markdown,
            Line = line,
            Column = column,
        };
    }

    /// <summary>
    /// Splits a target from its fragment. "#^" is tested before "#" because a block
    /// reference would otherwise parse as a heading anchor named "^id". Everything after
    /// the first "#" is kept, so nested heading paths ("Note#H1#H2") survive intact.
    /// </summary>
    public static (string Target, string? Anchor, bool IsBlockId) SplitAnchor(string value)
    {
        int blockMarker = value.IndexOf("#^", StringComparison.Ordinal);
        if (blockMarker >= 0)
        {
            return (value[..blockMarker].Trim(), value[(blockMarker + 2)..].Trim(), true);
        }

        int hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            string fragment = value[(hash + 1)..].Trim();
            return (value[..hash].Trim(), fragment.Length > 0 ? fragment : null, false);
        }

        return (value.Trim(), null, false);
    }

    /// <summary>Parses "300" or "300x200" from an embed's pipe payload.</summary>
    public static (double? Width, double? Height) ParseSizeSpec(string? sizeSpec)
    {
        if (sizeSpec is null)
        {
            return (null, null);
        }

        Match match = SizeSpecPattern().Match(sizeSpec);

        if (!match.Success)
        {
            return (null, null);
        }

        double width = double.Parse(match.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture);

        return match.Groups["h"].Success
            ? (width, double.Parse(match.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : (width, null);
    }

    [GeneratedRegex(@"^(?<w>\d+)(x(?<h>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SizeSpecPattern();
}
