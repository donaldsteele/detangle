using System.Text;
using System.Text.RegularExpressions;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Detangle.Core.Parsing;

/// <summary>
/// One pass over a document's text producing frontmatter, headings, block anchors and
/// links.
/// <para>
/// Extraction runs over Markdig's AST rather than over raw text, and that choice is
/// what settles most of plan.md section 3.2 for free: fenced and indented code, inline
/// code spans, HTML blocks and comments, and escaped <c>\[\[</c> brackets never become
/// literal inlines, so a wikilink inside any of them is never seen and never pollutes
/// the graph. Frontmatter is blanked (not removed) before parsing so that reported line
/// numbers still match the file on disk.
/// </para>
/// </summary>
public static partial class DocumentParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseWikiLinks()
        .Build();

    /// <summary>Parses document text. <paramref name="relativePath"/> stamps the links' source.</summary>
    public static ParsedDocument Parse(string relativePath, string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return ParsedDocument.Empty;
        }

        string text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿');
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(text);

        string body = BlankLeadingLines(text, frontmatter.LineCount);
        int[] lineStarts = BuildLineStarts(body);

        MarkdownDocument document = Markdown.Parse(body, Pipeline);

        var headings = new List<Heading>();
        var links = new List<LinkReference>();
        var slugger = new HeadingSlugger();

        foreach (MarkdownObject node in document.Descendants())
        {
            switch (node)
            {
                case HeadingBlock heading:
                    string headingText = ToPlainText(heading.Inline);
                    headings.Add(new Heading(
                        heading.Level, headingText, slugger.Slug(headingText), heading.Line + 1));
                    break;

                case LinkInline link:
                    LinkReference? reference = ReadMarkdownLink(relativePath, link, lineStarts);
                    if (reference is not null)
                    {
                        links.Add(reference);
                    }

                    break;

                case WikiLinkInline wikiLink:
                    LinkReference? parsed = ParseWikiLink(
                        relativePath, wikiLink.Body, wikiLink.IsEmbed, lineStarts, wikiLink.Span.Start);

                    if (parsed is not null)
                    {
                        links.Add(parsed);
                    }

                    break;

                case LiteralInline literal:
                    ReadInlineSyntax(relativePath, literal, lineStarts, links);
                    break;
            }
        }

        List<BlockAnchor> blockAnchors = ReadBlockAnchors(body, document);
        links.AddRange(ReadFrontmatterReferences(relativePath, frontmatter));

        return new ParsedDocument(frontmatter, headings, blockAnchors, links);
    }

    /// <summary>
    /// Replaces the first <paramref name="lineCount"/> lines with empty ones. Deleting
    /// them instead would shift every line number in the document by the size of a block
    /// that varies per file.
    /// </summary>
    private static string BlankLeadingLines(string text, int lineCount)
    {
        if (lineCount <= 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int line = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (line >= lineCount)
            {
                builder.Append(text, i, text.Length - i);
                break;
            }

            if (text[i] == '\n')
            {
                line++;
            }

            builder.Append(text[i] == '\n' ? '\n' : ' ');
        }

        return builder.ToString();
    }

    private static LinkReference? ReadMarkdownLink(string sourcePath, LinkInline link, int[] lineStarts)
    {
        string url = link.Url ?? string.Empty;

        if (url.Length == 0)
        {
            return null;
        }

        (int line, int column) = Locate(lineStarts, link.Span.Start);
        (string target, string? anchor, bool isBlockId) = SplitAnchor(url);

        string? label = ToPlainText(link).Trim();

        return new LinkReference
        {
            SourcePath = sourcePath,
            RawTarget = target,
            Label = label.Length > 0 ? label : null,
            Anchor = anchor,
            AnchorIsBlockId = isBlockId,
            IsEmbed = link.IsImage,
            Syntax = LinkSyntax.Markdown,
            Line = line,
            Column = column,
        };
    }

    /// <summary>
    /// Scans literal text for the syntaxes Markdig has no node for: wikilinks, Logseq
    /// block references, and tags. Offsets come from the literal's own slice, which
    /// indexes into the original string, so positions stay exact without a second scan.
    /// </summary>
    private static void ReadInlineSyntax(
        string sourcePath, LiteralInline literal, int[] lineStarts, List<LinkReference> links)
    {
        string content = literal.Content.ToString();

        if (content.Length == 0)
        {
            return;
        }

        int origin = literal.Content.Start;

        foreach (Match match in BlockReferencePattern().Matches(content))
        {
            (int line, int column) = Locate(lineStarts, origin + match.Index);

            links.Add(new LinkReference
            {
                SourcePath = sourcePath,
                RawTarget = string.Empty,
                Anchor = match.Groups["uuid"].Value,
                AnchorIsBlockId = true,
                Syntax = LinkSyntax.BlockReference,
                Line = line,
                Column = column,
            });
        }

        foreach (Match match in TagPattern().Matches(content))
        {
            string tag = match.Groups["tag"].Value;

            // "#2024" and "#1" are almost always headings-in-prose or issue numbers, not
            // tags; requiring a letter somewhere keeps them out of the graph.
            if (!tag.Any(char.IsLetter))
            {
                continue;
            }

            (int line, int column) = Locate(lineStarts, origin + match.Index);

            links.Add(new LinkReference
            {
                SourcePath = sourcePath,
                RawTarget = tag,
                Syntax = LinkSyntax.Tag,
                Line = line,
                Column = column,
            });
        }
    }

    /// <summary>
    /// Splits the inside of a wikilink into target, alias or size, and anchor.
    /// The "|" overload (plan.md section 3.2) resolves here: in an embed whose target
    /// has a non-markdown extension, the pipe payload is a size, not an alias.
    /// </summary>
    private static LinkReference? ParseWikiLink(
        string sourcePath, string body, bool isEmbed, int[] lineStarts, int sourceIndex)
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

        bool isSize = isEmbed
            && right is { Length: > 0 }
            && SizeSpecPattern().IsMatch(right)
            && !LinkNormalizer.HasMarkdownExtension(target);

        (int line, int column) = Locate(lineStarts, sourceIndex);

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

    /// <summary>
    /// Splits a target from its fragment. "#^" is tested before "#" because a block
    /// reference would otherwise parse as a heading anchor named "^id". Everything after
    /// the first "#" is kept, so nested heading paths ("Note#H1#H2") survive intact.
    /// </summary>
    internal static (string Target, string? Anchor, bool IsBlockId) SplitAnchor(string value)
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

    /// <summary>
    /// Finds "^blockid" end-of-line markers and Logseq "id:: uuid" properties. These are
    /// line-oriented rather than inline, so they are read from the text with the line
    /// ranges of every code and HTML block excluded.
    /// </summary>
    private static List<BlockAnchor> ReadBlockAnchors(string body, MarkdownDocument document)
    {
        var excluded = new HashSet<int>();

        foreach (MarkdownObject node in document.Descendants())
        {
            if (node is CodeBlock or HtmlBlock)
            {
                var block = (LeafBlock)node;
                int last = block.Line + Math.Max(0, block.Lines.Count - 1);

                // A fenced block's closing fence is not one of its content lines, so the
                // range is widened by one rather than trusting Lines.Count alone.
                for (int line = block.Line; line <= last + 1; line++)
                {
                    excluded.Add(line);
                }
            }
        }

        var anchors = new List<BlockAnchor>();
        string[] lines = body.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (excluded.Contains(i))
            {
                continue;
            }

            string line = lines[i];

            Match blockId = BlockIdPattern().Match(line);
            if (blockId.Success)
            {
                anchors.Add(new BlockAnchor(blockId.Groups["id"].Value, i + 1, IsUuid: false));
            }

            Match logseqId = LogseqIdPattern().Match(line);
            if (logseqId.Success)
            {
                anchors.Add(new BlockAnchor(logseqId.Groups["id"].Value, i + 1, IsUuid: true));
            }
        }

        return anchors;
    }

    /// <summary>
    /// Turns frontmatter reference keys into links. They carry no brackets, which is
    /// exactly why every other viewer drops them (plan.md section 3.3).
    /// </summary>
    private static IEnumerable<LinkReference> ReadFrontmatterReferences(
        string sourcePath, DocumentFrontmatter frontmatter)
    {
        foreach (string value in frontmatter.References)
        {
            string target = value.Trim();

            // A reference key may still hold a wikilink or a markdown link; unwrap both
            // so the graph sees one target either way.
            Match wiki = WikiLinkPattern().Match(target);
            if (wiki.Success)
            {
                target = wiki.Groups["body"].Value;
                int pipe = target.IndexOf('|', StringComparison.Ordinal);
                if (pipe >= 0)
                {
                    target = target[..pipe];
                }
            }
            else
            {
                Match markdown = MarkdownLinkPattern().Match(target);
                if (markdown.Success)
                {
                    target = markdown.Groups["url"].Value;
                }
            }

            (string cleaned, string? anchor, bool isBlockId) = SplitAnchor(target.Trim());

            if (cleaned.Length == 0 && anchor is null)
            {
                continue;
            }

            yield return new LinkReference
            {
                SourcePath = sourcePath,
                RawTarget = cleaned,
                Anchor = anchor,
                AnchorIsBlockId = isBlockId,
                Syntax = LinkSyntax.Frontmatter,
                Line = 1,
                Column = 0,
            };
        }
    }

    private static string ToPlainText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (Inline inline in container.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    /// <summary>Maps an absolute character offset to a 1-based line and 0-based column.</summary>
    private static (int Line, int Column) Locate(int[] lineStarts, int offset)
    {
        int index = Array.BinarySearch(lineStarts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, lineStarts.Length - 1);

        return (index + 1, offset - lineStarts[index]);
    }

    [GeneratedRegex(@"(?<embed>!)?\[\[(?<body>[^\[\]]*)\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkPattern();

    [GeneratedRegex(@"\(\((?<uuid>[0-9a-fA-F-]{8,})\)\)", RegexOptions.Compiled)]
    private static partial Regex BlockReferencePattern();

    [GeneratedRegex(@"(?<=^|\s)#(?<tag>[\wÀ-￿/-]+)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^\d+(x\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SizeSpecPattern();

    [GeneratedRegex(@"(?:^|\s)\^(?<id>[A-Za-z0-9][A-Za-z0-9_-]*)\s*$", RegexOptions.Compiled)]
    private static partial Regex BlockIdPattern();

    [GeneratedRegex(@"(?:^|\s)id::\s*(?<id>[0-9a-fA-F-]{8,})\s*$", RegexOptions.Compiled)]
    private static partial Regex LogseqIdPattern();

    [GeneratedRegex(@"\[[^\]]*\]\((?<url>[^)]*)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkPattern();
}
