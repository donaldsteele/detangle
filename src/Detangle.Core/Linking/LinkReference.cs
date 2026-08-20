namespace Detangle.Core.Linking;

/// <summary>Which syntax produced a link.</summary>
public enum LinkSyntax
{
    /// <summary><c>[[target]]</c>, with optional alias, anchor or embed marker.</summary>
    WikiLink,

    /// <summary><c>[label](target)</c> or the autolink form.</summary>
    Markdown,

    /// <summary>A bare slug in a frontmatter key such as "sources" or "related".</summary>
    Frontmatter,

    /// <summary>A Logseq block reference, <c>((uuid))</c>.</summary>
    BlockReference,

    /// <summary>A <c>#tag</c>, which Logseq treats as a page link.</summary>
    Tag,
}

/// <summary>
/// One link occurrence found in a document, after syntax has been stripped but before
/// resolution. Links inside code fences, inline code, HTML comments and frontmatter
/// strings never become a LinkReference at all (plan.md section 3.2) — excluding them
/// at extraction time is what keeps the graph honest.
/// </summary>
public sealed class LinkReference
{
    /// <summary>The document this link was found in, vault-relative with "/" separators.</summary>
    public required string SourcePath { get; init; }

    /// <summary>The target exactly as written, minus alias, anchor and embed syntax.</summary>
    public required string RawTarget { get; init; }

    /// <summary>
    /// The display text, from <c>[[target|alias]]</c> or <c>[label](target)</c>. Null
    /// when the link had none and the target doubles as the label.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// The fragment after "#" or "#^", without its marker. Null when absent. An anchor
    /// that fails to resolve still navigates to the file (section 5.6).
    /// </summary>
    public string? Anchor { get; init; }

    /// <summary>True when the anchor used the "#^blockid" form rather than a heading.</summary>
    public bool AnchorIsBlockId { get; init; }

    /// <summary>True for <c>![[…]]</c> and <c>![](…)</c>: the target is embedded, not linked.</summary>
    public bool IsEmbed { get; init; }

    /// <summary>
    /// The pipe payload when it is a size rather than an alias, as in
    /// <c>![[diagram.png|300]]</c>. The "|" overload is disambiguated by target
    /// extension at extraction time (plan.md section 3.2).
    /// </summary>
    public string? SizeSpec { get; init; }

    /// <summary>Which syntax produced this link.</summary>
    public required LinkSyntax Syntax { get; init; }

    /// <summary>1-based line in the source document.</summary>
    public int Line { get; init; }

    /// <summary>0-based column in the source line.</summary>
    public int Column { get; init; }

    /// <summary>
    /// True when the target is empty and the anchor points inside the current document,
    /// as in <c>[[#Heading]]</c>.
    /// </summary>
    public bool IsSelfReference => string.IsNullOrEmpty(RawTarget) && Anchor is not null;

    /// <summary>True for external targets, which the resolver never touches.</summary>
    public bool IsExternal =>
        RawTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || RawTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || RawTarget.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || RawTarget.StartsWith("//", StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() =>
        Anchor is null ? RawTarget : $"{RawTarget}#{(AnchorIsBlockId ? "^" : string.Empty)}{Anchor}";
}
