using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Detangle.Rendering.Markdown;

/// <summary>
/// A MkDocs / Python-Markdown admonition: "!!! note" or the collapsible "??? note".
/// </summary>
public sealed class AdmonitionBlock : ContainerBlock
{
    /// <summary>Creates the block.</summary>
    /// <param name="parser">The parser that opened it.</param>
    public AdmonitionBlock(BlockParser parser)
        : base(parser)
    {
    }

    /// <summary>The admonition kind, lowercased: note, warning, tip, and so on.</summary>
    public string Kind { get; set; } = "note";

    /// <summary>The quoted title, or null when the kind doubles as the title.</summary>
    public string? Title { get; set; }

    /// <summary>True for the "???" forms, which render with a disclosure triangle.</summary>
    public bool IsCollapsible { get; set; }

    /// <summary>True when a collapsible admonition opens closed — "???" without "+".</summary>
    public bool StartsCollapsed { get; set; }
}

/// <summary>
/// Parses MkDocs admonitions.
/// <para>
/// Markdig has no admonition parser and its custom-container extension only covers the
/// ":::" fence, so this is written out. The syntax is indentation-delimited rather than
/// fenced: the marker line opens the block and every following line indented by four
/// spaces belongs to it, which is why continuation strips exactly four columns and
/// hands the rest back to the normal block parsers.
/// </para>
/// </summary>
public sealed class AdmonitionBlockParser : BlockParser
{
    private const int ContentIndent = 4;

    /// <summary>Registers "!" and "?" as opening characters.</summary>
    public AdmonitionBlockParser()
    {
        OpeningCharacters = ['!', '?'];
    }

    /// <inheritdoc />
    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        StringSlice line = processor.Line;
        char marker = line.CurrentChar;
        int start = line.Start;

        if (line.PeekCharAbsolute(start + 1) != marker || line.PeekCharAbsolute(start + 2) != marker)
        {
            return BlockState.None;
        }

        int cursor = start + 3;
        bool collapsible = marker == '?';
        bool startsCollapsed = collapsible;

        if (line.PeekCharAbsolute(cursor) == '+')
        {
            // "???+" is collapsible but opens expanded.
            startsCollapsed = false;
            cursor++;
        }

        if (!IsSpace(line.PeekCharAbsolute(cursor)))
        {
            return BlockState.None;
        }

        cursor = SkipSpaces(line, cursor);

        int kindStart = cursor;
        while (IsKindCharacter(line.PeekCharAbsolute(cursor)))
        {
            cursor++;
        }

        if (cursor == kindStart)
        {
            return BlockState.None;
        }

        string kind = line.Text[kindStart..cursor].ToLowerInvariant();
        cursor = SkipSpaces(line, cursor);

        string? title = ReadQuotedTitle(line, ref cursor);

        var block = new AdmonitionBlock(this)
        {
            Kind = kind,
            Title = title,
            IsCollapsible = collapsible,
            StartsCollapsed = startsCollapsed,
            Column = processor.Column,
            Span = { Start = processor.Start },
        };

        processor.NewBlocks.Push(block);

        return BlockState.ContinueDiscard;
    }

    /// <inheritdoc />
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine)
        {
            // A blank line inside an admonition is a paragraph break, not the end of it —
            // the block ends at the first non-blank line that is not indented.
            return BlockState.Continue;
        }

        if (processor.Indent < ContentIndent)
        {
            return BlockState.None;
        }

        processor.GoToColumn(processor.ColumnBeforeIndent + ContentIndent);

        return BlockState.Continue;
    }

    private static string? ReadQuotedTitle(StringSlice line, ref int cursor)
    {
        char quote = line.PeekCharAbsolute(cursor);

        if (quote is not '"' and not '\'')
        {
            return null;
        }

        int titleStart = cursor + 1;
        int titleEnd = titleStart;

        while (titleEnd < line.End + 1 && line.PeekCharAbsolute(titleEnd) is var c && c != quote && c != '\0')
        {
            titleEnd++;
        }

        cursor = titleEnd + 1;
        string title = line.Text[titleStart..titleEnd];

        // MkDocs uses an empty title to mean "no title bar at all"; an empty string here
        // would render as a blank header, so it folds back to null.
        return title.Length == 0 ? null : title;
    }

    private static int SkipSpaces(StringSlice line, int cursor)
    {
        while (IsSpace(line.PeekCharAbsolute(cursor)))
        {
            cursor++;
        }

        return cursor;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t';

    private static bool IsKindCharacter(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';
}

/// <summary>Adds <see cref="AdmonitionBlockParser"/> to a Markdig pipeline.</summary>
public sealed class AdmonitionExtension : IMarkdownExtension
{
    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<AdmonitionBlockParser>())
        {
            // Before the thematic-break and paragraph parsers, which would otherwise claim
            // a line starting with "!!!" as ordinary text.
            pipeline.BlockParsers.Insert(0, new AdmonitionBlockParser());
        }
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Detangle renders from the AST into Avalonia controls; there is no HTML renderer
        // to extend.
    }
}

/// <summary>Pipeline builder extension method for MkDocs admonitions.</summary>
public static class AdmonitionPipelineExtensions
{
    /// <summary>Enables "!!! note" and "??? note" admonitions.</summary>
    public static MarkdownPipelineBuilder UseAdmonitions(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<AdmonitionExtension>();
        return pipeline;
    }
}
