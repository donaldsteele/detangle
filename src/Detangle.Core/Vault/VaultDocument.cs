using Detangle.Core.Linking;
using Detangle.Core.Parsing;

namespace Detangle.Core.Vault;

/// <summary>One heading in a document, with the anchor forms that can address it.</summary>
/// <param name="Level">1 for H1 through 6 for H6.</param>
/// <param name="Text">The raw heading text, which Obsidian matches case-insensitively.</param>
/// <param name="Slug">The github-slugger anchor, including any "-1"/"-2" dedup counter.</param>
/// <param name="Line">1-based line in the source document.</param>
public sealed record Heading(int Level, string Text, string Slug, int Line);

/// <summary>
/// A block-level anchor: either an Obsidian "^blockid" trailing marker or a Logseq
/// "id:: uuid" property.
/// </summary>
/// <param name="Id">The identifier without its marker.</param>
/// <param name="Line">1-based line in the source document.</param>
/// <param name="IsUuid">True for a Logseq "id::" uuid addressed as ((uuid)).</param>
public sealed record BlockAnchor(string Id, int Line, bool IsUuid);

/// <summary>
/// One file in the vault, after scanning. Documents are immutable once built; the file
/// watcher replaces them wholesale rather than mutating, so a resolution in flight
/// always sees a consistent snapshot.
/// </summary>
public sealed class VaultDocument
{
    /// <summary>Vault-relative path with "/" separators, in the case the filesystem reports.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Absolute path on disk, in the platform's own separator form.</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>
    /// Filename without its extension, original case. For Dendron this is the whole
    /// dot-hierarchy ("a.b.c"), because the last dot there is not an extension.
    /// </summary>
    public required string Stem { get; init; }

    /// <summary>File extension including the leading dot, lowercased. Empty when absent.</summary>
    public required string Extension { get; init; }

    /// <summary>Vault-relative directory with "/" separators. Empty for vault-root files.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Last write time, used by the Link Doctor's stale-link findings.</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>Size on disk, used by the oversized-file finding.</summary>
    public long SizeInBytes { get; init; }

    /// <summary>Normalized frontmatter; never null, <see cref="DocumentFrontmatter.Empty"/> when absent.</summary>
    public DocumentFrontmatter Frontmatter { get; init; } = DocumentFrontmatter.Empty;

    /// <summary>Headings in document order.</summary>
    public IReadOnlyList<Heading> Headings { get; init; } = [];

    /// <summary>Block anchors in document order.</summary>
    public IReadOnlyList<BlockAnchor> BlockAnchors { get; init; } = [];

    /// <summary>Outbound links, in document order, excluding code fences and comments.</summary>
    public IReadOnlyList<LinkReference> Links { get; init; } = [];

    /// <summary>True when this file is markdown rather than an attachment.</summary>
    public bool IsMarkdown => LinkNormalizer.HasMarkdownExtension(RelativePath);

    /// <summary>The first H1, used by chain step 7 and as a display-name fallback.</summary>
    public string? FirstHeading =>
        Headings.FirstOrDefault(h => h.Level == 1)?.Text ?? Headings.FirstOrDefault()?.Text;

    /// <summary>
    /// What the reader shows for this document. Flavors override the rule — Dendron
    /// prefers the frontmatter title over its dot-path filename (plan.md section 5.7).
    /// </summary>
    public string DisplayName => Frontmatter.Title ?? FirstHeading ?? Stem;

    /// <inheritdoc />
    public override string ToString() => RelativePath;
}
