using Detangle.Core.Linking;

namespace Detangle.Rendering.Model;

/// <summary>Character-level styling, combinable.</summary>
[Flags]
public enum TextStyle
{
    /// <summary>No styling.</summary>
    None = 0,

    /// <summary>Emphasis, rendered italic.</summary>
    Italic = 1 << 0,

    /// <summary>Strong emphasis, rendered bold.</summary>
    Bold = 1 << 1,

    /// <summary>"~~struck~~".</summary>
    Strikethrough = 1 << 2,

    /// <summary>"==highlighted==".</summary>
    Highlight = 1 << 3,

    /// <summary>"^superscript^".</summary>
    Superscript = 1 << 4,

    /// <summary>"~subscript~".</summary>
    Subscript = 1 << 5,
}

/// <summary>
/// One node of inline content in the render model. The model is deliberately a plain
/// immutable tree with no Avalonia types in it: everything about how a document should
/// look is decided here and tested here, and the control factory downstream is a
/// mechanical translation with no judgement of its own.
/// </summary>
public abstract record RenderInline;

/// <summary>Literal text.</summary>
/// <param name="Text">The text, with entities already decoded.</param>
public sealed record TextRun(string Text) : RenderInline;

/// <summary>Styled children — bold, italic, and the rest, which nest arbitrarily.</summary>
/// <param name="Style">The styling this span adds to its children.</param>
/// <param name="Children">The styled content.</param>
public sealed record StyleRun(TextStyle Style, IReadOnlyList<RenderInline> Children) : RenderInline;

/// <summary>An inline code span.</summary>
/// <param name="Code">The code text.</param>
public sealed record CodeRun(string Code) : RenderInline;

/// <summary>A hard or soft line break.</summary>
/// <param name="IsHard">True for an explicit break; soft breaks may be re-wrapped.</param>
public sealed record BreakRun(bool IsHard) : RenderInline;

/// <summary>
/// A link, carrying the resolution that produced its target. The resolution is what the
/// reader decorates with — dotted underline, warning icon, placeholder styling — so it
/// travels with the link rather than being looked up again at draw time.
/// </summary>
/// <param name="Resolution">The chain's answer for this link.</param>
/// <param name="Children">The display content.</param>
/// <param name="Url">The href for an external link; null for vault links.</param>
public sealed record LinkRun(
    LinkResolution Resolution,
    IReadOnlyList<RenderInline> Children,
    string? Url = null) : RenderInline;

/// <summary>An inline image or attachment embed.</summary>
/// <param name="Resolution">The chain's answer for the image target.</param>
/// <param name="AlternateText">Alt text, shown when the file cannot be loaded.</param>
/// <param name="Width">Requested width from the "|300" size syntax, if any.</param>
/// <param name="Height">Requested height from the "|300x200" size syntax, if any.</param>
public sealed record ImageRun(
    LinkResolution Resolution,
    string? AlternateText,
    double? Width = null,
    double? Height = null) : RenderInline;

/// <summary>Inline math, "$…$".</summary>
/// <param name="Source">The TeX source, without delimiters.</param>
public sealed record MathRun(string Source) : RenderInline;

/// <summary>A "#tag", which the tag browser and the graph both consume.</summary>
/// <param name="Tag">The tag without its leading "#".</param>
public sealed record TagRun(string Tag) : RenderInline;

/// <summary>A footnote reference, "[^1]".</summary>
/// <param name="Label">The footnote's display label.</param>
/// <param name="Order">1-based order of first appearance, which is what is shown.</param>
public sealed record FootnoteReferenceRun(string Label, int Order) : RenderInline;
