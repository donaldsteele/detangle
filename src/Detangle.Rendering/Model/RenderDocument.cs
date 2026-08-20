using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Rendering.Model;

/// <summary>
/// One document, ready to draw: its blocks, the resolutions the reader needs for the
/// status bar and the Link Doctor, and whatever went wrong on the way.
/// </summary>
/// <param name="Document">The vault document this was built from.</param>
/// <param name="Blocks">The document body, properties card first when there is one.</param>
/// <param name="Resolutions">Every link resolved while building, in document order.</param>
/// <param name="Diagnostics">Rendering problems: embed cycles, unreadable files.</param>
public sealed record RenderDocument(
    VaultDocument Document,
    IReadOnlyList<RenderBlock> Blocks,
    IReadOnlyList<LinkResolution> Resolutions,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Links that reached step 13 without a target.</summary>
    public IEnumerable<LinkResolution> BrokenLinks =>
        Resolutions.Where(r => r.IsUnresolved);

    /// <summary>Links that matched more than one document.</summary>
    public IEnumerable<LinkResolution> AmbiguousLinks =>
        Resolutions.Where(r => r.IsAmbiguous);

    /// <summary>Headings in document order, for the outline pane.</summary>
    public IEnumerable<HeadingRenderBlock> Outline => Flatten(Blocks).OfType<HeadingRenderBlock>();

    private static IEnumerable<RenderBlock> Flatten(IEnumerable<RenderBlock> blocks)
    {
        foreach (RenderBlock block in blocks)
        {
            yield return block;

            IEnumerable<RenderBlock> children = block switch
            {
                QuoteRenderBlock quote => quote.Blocks,
                CalloutRenderBlock callout => callout.Blocks,
                ListRenderBlock list => list.Items,
                ListItemRenderBlock item => item.Blocks,
                TableRenderBlock table => table.Rows,
                TableRowRenderBlock row => row.Cells,
                TableCellRenderBlock cell => cell.Blocks,
                // Transclusions are deliberately excluded: an embedded document's headings
                // belong to that document's outline, not to this one's.
                _ => [],
            };

            foreach (RenderBlock child in Flatten(children))
            {
                yield return child;
            }
        }
    }
}
