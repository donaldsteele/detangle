using Detangle.Core.Linking;
using Detangle.Core.Parsing;

namespace Detangle.Rendering.Model;

/// <summary>Horizontal alignment of a table column.</summary>
public enum ColumnAlignment
{
    /// <summary>No alignment specified.</summary>
    None,

    /// <summary>Left aligned.</summary>
    Left,

    /// <summary>Centered.</summary>
    Center,

    /// <summary>Right aligned.</summary>
    Right,
}

/// <summary>The checkbox state of a task list item.</summary>
public enum TaskState
{
    /// <summary>Not a task item.</summary>
    None,

    /// <summary>"- [ ]".</summary>
    Unchecked,

    /// <summary>"- [x]".</summary>
    Checked,
}

/// <summary>
/// The dialect a callout was written in. Both are supported because LLM-written wikis
/// mix them freely — the same vault will hold Obsidian blockquote callouts and MkDocs
/// admonitions, often on the same page (plan.md section 6.1).
/// </summary>
public enum CalloutDialect
{
    /// <summary>"&gt; [!note]" — Obsidian.</summary>
    Obsidian,

    /// <summary>"!!! note" and "??? note" — MkDocs / Python-Markdown.</summary>
    MkDocs,
}

/// <summary>One node of block content in the render model.</summary>
public abstract record RenderBlock;

/// <summary>A paragraph.</summary>
/// <param name="Inlines">The paragraph's content.</param>
public sealed record ParagraphRenderBlock(IReadOnlyList<RenderInline> Inlines) : RenderBlock;

/// <summary>A heading, with the anchor that addresses it.</summary>
/// <param name="Level">1 through 6.</param>
/// <param name="Inlines">The heading content.</param>
/// <param name="Slug">The github-slugger anchor, including any dedup counter.</param>
/// <param name="Text">Plain text of the heading, for the outline pane.</param>
public sealed record HeadingRenderBlock(
    int Level,
    IReadOnlyList<RenderInline> Inlines,
    string Slug,
    string Text) : RenderBlock;

/// <summary>
/// A fenced or indented code block. Diagram fences keep their source here too: phase 3
/// replaces them with a rendered SVG, and until then they display as code rather than
/// disappearing.
/// </summary>
/// <param name="Language">The info string's first word, lowercased. Empty when absent.</param>
/// <param name="Source">The code, without the fences.</param>
/// <param name="IsDiagram">True for a language the diagram renderer will claim.</param>
public sealed record CodeRenderBlock(string Language, string Source, bool IsDiagram) : RenderBlock;

/// <summary>A blockquote that is not a callout.</summary>
/// <param name="Blocks">The quoted content.</param>
public sealed record QuoteRenderBlock(IReadOnlyList<RenderBlock> Blocks) : RenderBlock;

/// <summary>
/// A callout or admonition.
/// </summary>
/// <param name="Kind">The callout kind, lowercased: note, warning, tip, and so on.</param>
/// <param name="Title">The title line; defaults to the kind when none was written.</param>
/// <param name="Dialect">Which syntax produced it.</param>
/// <param name="IsCollapsible">True for "&gt; [!note]-" and for "??? note".</param>
/// <param name="StartsCollapsed">True when the collapsible callout opens closed.</param>
/// <param name="Blocks">The callout body.</param>
public sealed record CalloutRenderBlock(
    string Kind,
    string Title,
    CalloutDialect Dialect,
    bool IsCollapsible,
    bool StartsCollapsed,
    IReadOnlyList<RenderBlock> Blocks) : RenderBlock;

/// <summary>One item of a list.</summary>
/// <param name="Blocks">The item's content.</param>
/// <param name="Task">Checkbox state for task list items.</param>
public sealed record ListItemRenderBlock(IReadOnlyList<RenderBlock> Blocks, TaskState Task) : RenderBlock;

/// <summary>An ordered or unordered list.</summary>
/// <param name="IsOrdered">True for a numbered list.</param>
/// <param name="Start">First number of an ordered list.</param>
/// <param name="Items">The items.</param>
public sealed record ListRenderBlock(
    bool IsOrdered,
    int Start,
    IReadOnlyList<ListItemRenderBlock> Items) : RenderBlock;

/// <summary>One table cell.</summary>
/// <param name="Blocks">The cell's content.</param>
/// <param name="ColumnSpan">How many columns this cell spans.</param>
public sealed record TableCellRenderBlock(IReadOnlyList<RenderBlock> Blocks, int ColumnSpan = 1) : RenderBlock;

/// <summary>One table row.</summary>
/// <param name="Cells">The row's cells.</param>
/// <param name="IsHeader">True for the header row.</param>
public sealed record TableRowRenderBlock(IReadOnlyList<TableCellRenderBlock> Cells, bool IsHeader) : RenderBlock;

/// <summary>A pipe table.</summary>
/// <param name="Rows">Header row first, when present.</param>
/// <param name="Alignments">Per-column alignment.</param>
public sealed record TableRenderBlock(
    IReadOnlyList<TableRowRenderBlock> Rows,
    IReadOnlyList<ColumnAlignment> Alignments) : RenderBlock;

/// <summary>A horizontal rule.</summary>
public sealed record ThematicBreakRenderBlock : RenderBlock;

/// <summary>Block math, "$$…$$".</summary>
/// <param name="Source">The TeX source, without delimiters.</param>
public sealed record MathRenderBlock(string Source) : RenderBlock;

/// <summary>A definition list: one entry per term.</summary>
/// <param name="Items">The terms, in document order.</param>
public sealed record DefinitionListRenderBlock(IReadOnlyList<DefinitionRenderBlock> Items) : RenderBlock;

/// <summary>A definition list term and its definitions.</summary>
/// <param name="Term">The term being defined.</param>
/// <param name="Definitions">One entry per definition.</param>
public sealed record DefinitionRenderBlock(
    IReadOnlyList<RenderInline> Term,
    IReadOnlyList<IReadOnlyList<RenderBlock>> Definitions) : RenderBlock;

/// <summary>The footnotes collected from a document, rendered at its end.</summary>
/// <param name="Notes">Each note's label and content, in order of first reference.</param>
public sealed record FootnotesRenderBlock(
    IReadOnlyList<(string Label, IReadOnlyList<RenderBlock> Blocks)> Notes) : RenderBlock;

/// <summary>
/// An embedded document, from "![[note]]", "![[note#heading]]" or "![[note#^block]]".
/// The embedded content is inlined rather than linked, and carries a chip naming its
/// source so the reader can tell borrowed text from the page's own.
/// </summary>
/// <param name="Resolution">The chain's answer for the embed target.</param>
/// <param name="Blocks">The embedded content, already narrowed to the anchored section.</param>
/// <param name="Error">Why the embed is empty, when it is.</param>
public sealed record TransclusionRenderBlock(
    LinkResolution Resolution,
    IReadOnlyList<RenderBlock> Blocks,
    string? Error = null) : RenderBlock;

/// <summary>
/// The typed properties card built from a document's frontmatter, rendered above the
/// body. Frontmatter reference keys are links, so they arrive here already resolved.
/// </summary>
/// <param name="Frontmatter">The normalized frontmatter.</param>
/// <param name="References">Resolutions for the reference keys, in frontmatter order.</param>
public sealed record PropertiesRenderBlock(
    DocumentFrontmatter Frontmatter,
    IReadOnlyList<LinkResolution> References) : RenderBlock;
