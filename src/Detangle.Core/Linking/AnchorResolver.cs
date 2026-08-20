using System.Globalization;
using System.Text.RegularExpressions;
using Detangle.Core.Vault;

namespace Detangle.Core.Linking;

/// <summary>
/// Matches a link's fragment inside the document it resolved to (plan.md section 5.6).
/// <para>
/// The rule that matters most here is the failure rule: an anchor that matches nothing
/// still navigates to the file and only raises a warning. A fragment is a refinement of
/// a link, never a precondition for it, and treating it otherwise is how viewers turn a
/// renamed heading into a dead page.
/// </para>
/// </summary>
public static partial class AnchorResolver
{
    /// <summary>Matches a fragment against a document's headings and block anchors.</summary>
    /// <param name="document">The already-resolved target document.</param>
    /// <param name="anchor">The fragment without its "#" or "#^" marker.</param>
    /// <param name="isBlockId">True when the fragment used the "#^" block form.</param>
    public static AnchorResolution Resolve(VaultDocument document, string? anchor, bool isBlockId)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return AnchorResolution.None;
        }

        string fragment = anchor.Trim();

        if (isBlockId)
        {
            return ResolveBlock(document, fragment);
        }

        Match lineRange = LineRangePattern().Match(fragment);
        if (lineRange.Success)
        {
            // "#L10-L20" cites source lines in a repository file; it is not a wiki anchor
            // and must never be reported as a broken heading.
            int start = int.Parse(lineRange.Groups["start"].Value, CultureInfo.InvariantCulture);
            int? end = lineRange.Groups["end"].Success
                ? int.Parse(lineRange.Groups["end"].Value, CultureInfo.InvariantCulture)
                : null;

            return new AnchorResolution(AnchorRule.LineRange, start, end);
        }

        if (DocumentParameterPattern().IsMatch(fragment))
        {
            return new AnchorResolution(AnchorRule.DocumentParameter);
        }

        // A nested heading path "H1#H2" addresses the last heading in the path; the
        // intermediate ones are context, and Obsidian does not require them to nest.
        string leaf = fragment.Contains('#', StringComparison.Ordinal)
            ? fragment[(fragment.LastIndexOf('#') + 1)..].Trim()
            : fragment;

        foreach (string candidate in leaf == fragment ? [fragment] : new[] { fragment, leaf })
        {
            Heading? raw = document.Headings.FirstOrDefault(
                h => string.Equals(h.Text.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

            if (raw is not null)
            {
                return new AnchorResolution(AnchorRule.RawHeading, raw.Line);
            }
        }

        string slug = HeadingSlugger.SlugCore(leaf);

        Heading? bySlug = document.Headings.FirstOrDefault(
            h => string.Equals(h.Slug, leaf, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.Slug, slug, StringComparison.OrdinalIgnoreCase));

        if (bySlug is not null)
        {
            return new AnchorResolution(AnchorRule.HeadingSlug, bySlug.Line);
        }

        // Obsidian's "#^id" is the documented block form, but links written by hand and by
        // models drop the caret often enough to be worth one more probe before failing.
        AnchorResolution block = ResolveBlock(document, leaf);

        return block.IsResolved
            ? block
            : new AnchorResolution(
                AnchorRule.Unresolved,
                Warning: $"No heading or block \"{fragment}\" in {document.RelativePath}.");
    }

    private static AnchorResolution ResolveBlock(VaultDocument document, string id)
    {
        BlockAnchor? marker = document.BlockAnchors.FirstOrDefault(
            a => !a.IsUuid && string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        if (marker is not null)
        {
            return new AnchorResolution(AnchorRule.BlockId, marker.Line);
        }

        BlockAnchor? uuid = document.BlockAnchors.FirstOrDefault(
            a => a.IsUuid && string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        return uuid is not null
            ? new AnchorResolution(AnchorRule.BlockUuid, uuid.Line)
            : new AnchorResolution(
                AnchorRule.Unresolved,
                Warning: $"No block \"{id}\" in {document.RelativePath}.");
    }

    /// <summary>A GitHub-style code citation: "#L10" or "#L10-L20".</summary>
    [GeneratedRegex(@"^L(?<start>\d+)(?:-L?(?<end>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LineRangePattern();

    /// <summary>A PDF viewer parameter: "page=3", "height=400".</summary>
    [GeneratedRegex(@"^(page|height|width|zoom)=\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DocumentParameterPattern();
}
