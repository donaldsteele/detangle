using Detangle.Core.Vault;

namespace Detangle.Core.Linking;

/// <summary>How an anchor was matched inside the resolved document (plan.md section 5.6).</summary>
public enum AnchorRule
{
    /// <summary>The link carried no fragment.</summary>
    None,

    /// <summary>Raw heading text, case-insensitive and trimmed — the Obsidian rule.</summary>
    RawHeading,

    /// <summary>github-slugger slug, including "-1"/"-2" dedup counters.</summary>
    HeadingSlug,

    /// <summary>An Obsidian "^blockid" trailing marker.</summary>
    BlockId,

    /// <summary>A Logseq "id:: uuid" property, addressed as ((uuid)).</summary>
    BlockUuid,

    /// <summary>A "#L10-L20" code citation, which addresses lines rather than a heading.</summary>
    LineRange,

    /// <summary>A PDF "#page=N" or "#height=N" fragment.</summary>
    DocumentParameter,

    /// <summary>The fragment matched nothing. Navigation still succeeds, with a warning.</summary>
    Unresolved,
}

/// <summary>The outcome of matching a link's fragment inside its target document.</summary>
/// <param name="Rule">Which anchor form matched.</param>
/// <param name="Line">1-based line to scroll to, when known.</param>
/// <param name="EndLine">Last line of a "#L10-L20" range.</param>
/// <param name="Warning">Why the anchor failed, when it did.</param>
public sealed record AnchorResolution(
    AnchorRule Rule,
    int? Line = null,
    int? EndLine = null,
    string? Warning = null)
{
    /// <summary>The result for a link with no fragment.</summary>
    public static AnchorResolution None { get; } = new(AnchorRule.None);

    /// <summary>True when the fragment addressed something real.</summary>
    public bool IsResolved => Rule is not AnchorRule.None and not AnchorRule.Unresolved;
}

/// <summary>
/// One link, resolved. Carries the provenance the reader decorates with and the Link
/// Doctor groups by — which rule fired, what else it could have been, and what the
/// fuzzy step would have suggested (plan.md section 5.5).
/// </summary>
public sealed class LinkResolution
{
    /// <summary>The link this resolution is for.</summary>
    public required LinkReference Link { get; init; }

    /// <summary>The chosen document, or null for external, self-referential and unresolved links.</summary>
    public VaultDocument? Target { get; init; }

    /// <summary>Which chain step produced <see cref="Target"/>.</summary>
    public required ResolutionRule Rule { get; init; }

    /// <summary>
    /// Every document the winning step matched, in ambiguity-policy order. One entry on a
    /// clean hit; more than one means the warning and the disambiguation picker.
    /// </summary>
    public IReadOnlyList<VaultDocument> Candidates { get; init; } = [];

    /// <summary>
    /// Near misses from chain step 12, offered when nothing resolved. Never navigated to
    /// automatically — that is the whole point of keeping fuzzy matching separate.
    /// </summary>
    public IReadOnlyList<VaultDocument> Suggestions { get; init; } = [];

    /// <summary>How the fragment resolved inside the target.</summary>
    public AnchorResolution Anchor { get; init; } = AnchorResolution.None;

    /// <summary>True when more than one document matched and one was chosen deterministically.</summary>
    public bool IsAmbiguous => Candidates.Count > 1;

    /// <summary>True when the chain reached step 13 without a target.</summary>
    public bool IsUnresolved => Target is null && Rule is ResolutionRule.Placeholder;

    /// <summary>How much decoration the reader gives this link.</summary>
    public ResolutionConfidence Confidence => Rule switch
    {
        ResolutionRule.RememberedChoice => ResolutionConfidence.Exact,
        ResolutionRule.ExactVaultPath or ResolutionRule.NoteRelativePath
            or ResolutionRule.CaseSensitiveStem => ResolutionConfidence.Exact,
        ResolutionRule.PathSuffix or ResolutionRule.CaseInsensitiveStem
            or ResolutionRule.NormalizedName or ResolutionRule.Alias
            or ResolutionRule.Identifier => ResolutionConfidence.Normalized,
        ResolutionRule.FolderIndex or ResolutionRule.EncodingVariant
            or ResolutionRule.ExtensionProbe => ResolutionConfidence.Heuristic,
        ResolutionRule.FuzzyNearest => ResolutionConfidence.Suggestion,
        _ => ResolutionConfidence.Unresolved,
    };

    /// <summary>
    /// The hover explanation shown for any link that did not resolve exactly, e.g.
    /// "resolved by normalized-name match to concepts/attention.md".
    /// </summary>
    public string Explain() => Rule switch
    {
        ResolutionRule.NotAttempted => "not a vault link",
        ResolutionRule.Placeholder => $"\"{Link.RawTarget}\" matches no file in this vault",
        _ when Target is null => $"\"{Link.RawTarget}\" was not resolved",
        _ => $"resolved by {Describe(Rule)} to {Target.RelativePath}",
    };

    private static string Describe(ResolutionRule rule) => rule switch
    {
        ResolutionRule.ExactVaultPath => "exact path",
        ResolutionRule.NoteRelativePath => "note-relative path",
        ResolutionRule.CaseSensitiveStem => "filename match",
        ResolutionRule.PathSuffix => "path-suffix match",
        ResolutionRule.CaseInsensitiveStem => "case-insensitive filename match",
        ResolutionRule.NormalizedName => "normalized-name match",
        ResolutionRule.Alias => "alias match",
        ResolutionRule.Identifier => "identifier match",
        ResolutionRule.FolderIndex => "folder index",
        ResolutionRule.EncodingVariant => "encoding-variant match",
        ResolutionRule.ExtensionProbe => "attachment search",
        ResolutionRule.FuzzyNearest => "nearest-name suggestion",
        ResolutionRule.RememberedChoice => "your remembered choice",
        _ => "no rule",
    };
}
