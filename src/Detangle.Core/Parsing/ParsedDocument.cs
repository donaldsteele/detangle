using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Parsing;

/// <summary>
/// Everything one pass over a document's text yields. Kept separate from
/// <see cref="VaultDocument"/> so parsing can be tested on a string with no vault,
/// no filesystem and no scan.
/// </summary>
/// <param name="Frontmatter">The normalized frontmatter block.</param>
/// <param name="Headings">Headings in document order, with dedup-counted anchors.</param>
/// <param name="BlockAnchors">"^blockid" markers and Logseq "id::" uuids.</param>
/// <param name="Links">Outbound links, excluding code, comments and escaped brackets.</param>
public sealed record ParsedDocument(
    DocumentFrontmatter Frontmatter,
    IReadOnlyList<Heading> Headings,
    IReadOnlyList<BlockAnchor> BlockAnchors,
    IReadOnlyList<LinkReference> Links)
{
    /// <summary>An empty parse, used for attachments and unreadable files.</summary>
    public static ParsedDocument Empty { get; } =
        new(DocumentFrontmatter.Empty, [], [], []);
}
