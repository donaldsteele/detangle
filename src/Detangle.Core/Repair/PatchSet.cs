using System.Globalization;
using System.Text;
using Detangle.Core.Diagnostics;

namespace Detangle.Core.Repair;

/// <summary>One line the repair would change.</summary>
/// <param name="Line">The 1-based line number in the file as it stands.</param>
/// <param name="Before">The line as written.</param>
/// <param name="After">The line as it would be.</param>
/// <param name="Rule">The rung that resolved the link, which is why the rewrite is known.</param>
/// <param name="RawTarget">The link's target, exactly as written.</param>
/// <param name="Links">
/// How many links this one edit covers. Usually one; more when a line holds several, which
/// are coalesced into a single hunk so the patch cannot apply the first rewrite twice. A
/// caller reporting "what will change" wants the links, not the lines.
/// </param>
public sealed record Hunk(
    int Line, string Before, string After, string Rule, string RawTarget, int Links = 1);

/// <summary>Every change the repair would make to one file.</summary>
/// <param name="RelativePath">The document, vault-relative.</param>
/// <param name="ContentHash">
/// A hash of the text the plan was made against. Anything applying this later has to check
/// it: a patch computed against one version of a file and applied to another is how a
/// repair silently corrupts a page.
/// </param>
/// <param name="UsesCrLf">Whether the file's line endings are Windows ones.</param>
/// <param name="Hunks">The changed lines, in file order.</param>
public sealed record FilePatch(
    string RelativePath,
    string ContentHash,
    bool UsesCrLf,
    IReadOnlyList<Hunk> Hunks)
{
    /// <summary>How many links this file's hunks rewrite between them.</summary>
    public int LinkCount => Hunks.Sum(h => h.Links);
}

/// <summary>
/// A repair, planned but not applied (plan.md section 6.3).
/// <para>
/// Everything needed to compose a fix across a vault already existed in this assembly —
/// the Link Doctor's suggestion, its rewriter, the normalizer — but the only thing that
/// put them together was a method on an Avalonia view model. So continuous integration,
/// scripts and the agent that wrote the wiki could all be told it was broken, and none of
/// them could be told how to repair it.
/// </para>
/// <para>
/// A plan rather than a write. The CLI emits this as a unified diff and never touches the
/// vault, which keeps the promise the documentation already made; applying it is a
/// separate step that wants an undo behind it before it exists.
/// </para>
/// </summary>
/// <param name="Patches">The files that would change, by path.</param>
/// <param name="Policy">What was considered repairable.</param>
public sealed record PatchSet(IReadOnlyList<FilePatch> Patches, RepairPolicy Policy)
{
    /// <summary>A plan that would change nothing.</summary>
    public static PatchSet Empty { get; } = new([], RepairPolicy.Safe);

    /// <summary>How many files the plan would rewrite.</summary>
    public int FileCount => Patches.Count;

    /// <summary>How many lines the plan would change.</summary>
    public int HunkCount => Patches.Sum(p => p.Hunks.Count);

    /// <summary>How many links the plan would rewrite, which is not the same as lines.</summary>
    public int LinkCount => Patches.Sum(p => p.LinkCount);

    /// <summary>True when there is nothing to do.</summary>
    public bool IsEmpty => Patches.Count == 0;

    /// <summary>One line a person can act on.</summary>
    public string Summary() => IsEmpty
        ? "Nothing to repair."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{LinkCount} link{(LinkCount == 1 ? string.Empty : "s")} in "
                + $"{FileCount} file{(FileCount == 1 ? string.Empty : "s")}");

    /// <summary>
    /// The plan as a unified diff, which is the format every tool that reads a patch
    /// already reads. Each hunk carries the rung that resolved the link above it, so the
    /// diff answers "why" as well as "what" — which is the part no other link checker's
    /// output can.
    /// </summary>
    public string ToUnifiedDiff()
    {
        var diff = new StringBuilder();

        foreach (FilePatch patch in Patches)
        {
            diff.Append("--- a/").AppendLine(patch.RelativePath);
            diff.Append("+++ b/").AppendLine(patch.RelativePath);

            foreach (Hunk hunk in patch.Hunks)
            {
                // One line of context either side would need the file, which this type
                // deliberately does not hold. A single-line hunk is still a valid unified
                // diff, and the provenance comment is worth more than the context.
                diff.Append(CultureInfo.InvariantCulture, $"@@ -{hunk.Line},1 +{hunk.Line},1 @@");
                diff.Append(CultureInfo.InvariantCulture, $" resolved by {hunk.Rule} ({hunk.RawTarget})");
                diff.AppendLine();
                diff.Append('-').AppendLine(hunk.Before);
                diff.Append('+').AppendLine(hunk.After);
            }
        }

        return diff.ToString();
    }
}
