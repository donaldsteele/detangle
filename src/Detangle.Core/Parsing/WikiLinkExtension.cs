using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace Detangle.Core.Parsing;

/// <summary>
/// A parsed <c>[[target|label]]</c> or <c>![[target]]</c>, as a first-class inline.
/// </summary>
public sealed class WikiLinkInline : LeafInline
{
    /// <summary>The text between the brackets, verbatim.</summary>
    public required string Body { get; init; }

    /// <summary>True when the link was written as an embed, with a leading "!".</summary>
    public bool IsEmbed { get; init; }
}

/// <summary>
/// Parses wikilinks into the Markdig AST.
/// <para>
/// This has to be a real inline parser rather than a regex over the rendered text.
/// Markdig splits an unmatched "[[" into separate literal inlines, so a regex sees
/// fragments and finds nothing; and scanning the raw source instead would resurrect
/// every link inside a code fence, an inline code span or an HTML comment — exactly the
/// exclusions plan.md section 3.2 requires. Parsing in the pipeline gets both right for
/// free, because the parser is never invoked in those contexts.
/// </para>
/// </summary>
public sealed class WikiLinkParser : InlineParser
{
    /// <summary>
    /// Registers "[" and "!". The "!" matters: Markdig's own link parser opens on it and
    /// would consume the "![" of an embed before this parser ever saw the brackets.
    /// </summary>
    public WikiLinkParser()
    {
        OpeningCharacters = ['[', '!'];
    }

    /// <inheritdoc />
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        int start = slice.Start;
        bool isEmbed = slice.CurrentChar == '!';
        int open = isEmbed ? start + 1 : start;

        if (slice.PeekCharAbsolute(open) != '[' || slice.PeekCharAbsolute(open + 1) != '[')
        {
            // A single "[" is an ordinary markdown link, and a lone "!" is an image or
            // just punctuation; both belong to Markdig's own parsers.
            return false;
        }

        int cursor = open + 2;
        int end = -1;

        while (cursor < slice.End)
        {
            char current = slice.PeekCharAbsolute(cursor);

            if (current == '\n')
            {
                // Wikilinks do not span lines; bailing out here stops a stray "[[" from
                // swallowing the rest of a paragraph.
                return false;
            }

            if (current == ']' && slice.PeekCharAbsolute(cursor + 1) == ']')
            {
                end = cursor;
                break;
            }

            if (current == '[' && slice.PeekCharAbsolute(cursor + 1) == '[')
            {
                return false;
            }

            cursor++;
        }

        if (end < 0)
        {
            return false;
        }

        string body = slice.Text.Substring(open + 2, end - open - 2);

        var inline = new WikiLinkInline
        {
            Body = body,
            IsEmbed = isEmbed,
            Span = { Start = processor.GetSourcePosition(start, out int line, out int column) },
            Line = line,
            Column = column,
        };

        inline.Span.End = inline.Span.Start + (end + 2 - start) - 1;

        processor.Inline = inline;
        slice.Start = end + 2;

        return true;
    }
}

/// <summary>Adds <see cref="WikiLinkParser"/> to a Markdig pipeline.</summary>
public sealed class WikiLinkExtension : IMarkdownExtension
{
    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<WikiLinkParser>())
        {
            // Ahead of the link parser: "[[a]]" must not be read as a markdown link whose
            // text is "[a".
            pipeline.InlineParsers.Insert(0, new WikiLinkParser());
        }
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Rendering is phase 2's problem; extraction only needs the parser.
    }
}

/// <summary>Pipeline builder extension method for wikilink support.</summary>
public static class WikiLinkPipelineExtensions
{
    /// <summary>Enables <c>[[wikilink]]</c> parsing.</summary>
    public static MarkdownPipelineBuilder UseWikiLinks(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<WikiLinkExtension>();
        return pipeline;
    }
}
