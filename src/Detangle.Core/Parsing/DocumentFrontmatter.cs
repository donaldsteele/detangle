namespace Detangle.Core.Parsing;

/// <summary>The delimiter style a document's frontmatter block used.</summary>
public enum FrontmatterKind
{
    /// <summary>No frontmatter block was present.</summary>
    None,

    /// <summary>YAML between "---" fences. The overwhelming default.</summary>
    Yaml,

    /// <summary>TOML between "+++" fences (Hugo).</summary>
    Toml,

    /// <summary>JSON between ";;;" fences.</summary>
    Json,

    /// <summary>Logseq / Dataview "key:: value" lines in the first block.</summary>
    DoubleColon,
}

/// <summary>
/// The normalized frontmatter of one document. plan.md section 3.3 lists the key
/// union: the same concept appears under half a dozen spellings across the thirteen
/// formats, so the reader folds them into these fields and keeps everything it did
/// not recognise in <see cref="Extra"/> for the properties card.
/// </summary>
public sealed class DocumentFrontmatter
{
    /// <summary>An empty block, shared by every document that has no frontmatter.</summary>
    public static readonly DocumentFrontmatter Empty = new() { Kind = FrontmatterKind.None };

    /// <summary>Which delimiter style produced this block.</summary>
    public required FrontmatterKind Kind { get; init; }

    /// <summary>Display title, from "title".</summary>
    public string? Title { get; init; }

    /// <summary>Union of "aliases", "alias" and "aka".</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Union of "tags", "tag", "keywords" and "categories", with any leading "#" stripped.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Union of "id", "uid", "zettel-id" and "permalink".</summary>
    public string? Id { get; init; }

    /// <summary>Docusaurus "slug", which overrides the id-derived URL.</summary>
    public string? Slug { get; init; }

    /// <summary>Union of "type" and "kind".</summary>
    public string? Type { get; init; }

    /// <summary>Union of "status" and "state".</summary>
    public string? Status { get; init; }

    /// <summary>Union of "created", "date" and "dateCreated".</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Union of "updated", "modified" and "last_modified_at".</summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>
    /// Bare slugs from "sources", "related", "links", "see-also", "refs", "parent" and
    /// "up". These are links even though they carry no wikilink brackets, and dropping
    /// them is what makes other viewers under-report the graph.
    /// </summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>Union of "authors" and "author".</summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>The "url" key, when the note points at an external source.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// True when the document is hidden from a published build. "draft: true" and
    /// "publish: false" mean the same thing with inverted polarity, so both fold here.
    /// </summary>
    public bool IsDraft { get; init; }

    /// <summary>Union of "sidebar_position", "order", "weight" and "nav_order".</summary>
    public double? Order { get; init; }

    /// <summary>Obsidian "cssclasses".</summary>
    public IReadOnlyList<string> CssClasses { get; init; } = [];

    /// <summary>Every key the union did not claim, kept verbatim for the properties card.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Diagnostics from a malformed block; a parse failure is never fatal.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Number of lines the block occupied, so link positions stay accurate.</summary>
    public int LineCount { get; init; }
}
