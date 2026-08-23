using Detangle.Core.Diagnostics;

namespace Detangle.Core.Repair;

/// <summary>How much of a repair to plan.</summary>
public enum RepairScope
{
    /// <summary>
    /// Only links that resolved through rungs 4 to 8 and have exactly one canonical form.
    /// The set the panel's "Fix all safe" applies, and the only one where the rewrite is
    /// mechanical rather than a judgement.
    /// </summary>
    SafeOnly,

    /// <summary>
    /// Everything with a suggested rewrite, including the edit-distance guesses at broken
    /// links. Never mechanical: this is a plan for a person to read.
    /// </summary>
    All,
}

/// <summary>
/// What counts as "safe" to repair, in one place.
/// <para>
/// It exists so the panel, the CLI and anything later that applies a patch cannot drift
/// apart on the question. Before this, "safe" was a static method in the Link Doctor that
/// only the panel called, and a second caller would have been a second definition.
/// </para>
/// </summary>
/// <param name="Scope">How much to plan.</param>
public sealed record RepairPolicy(RepairScope Scope = RepairScope.SafeOnly)
{
    /// <summary>The default: mechanical rewrites only.</summary>
    public static RepairPolicy Safe { get; } = new(RepairScope.SafeOnly);

    /// <summary>True when this policy would repair the given finding.</summary>
    /// <param name="finding">The finding to test.</param>
    public bool Includes(Finding finding)
    {
        if (finding.SuggestedRewrite is not { Length: > 0 })
        {
            return false;
        }

        // A broken or drifted anchor is not repairable yet whatever the scope says: those
        // findings carry a suggested *heading*, and the rewriter replaces a link's target.
        // Repairing a fragment is its own change and wants an undo behind it first.
        return Scope == RepairScope.SafeOnly
            ? finding.Kind == FindingKind.NonCanonicalLink
            : finding.Kind is FindingKind.NonCanonicalLink or FindingKind.BrokenLink;
    }

    /// <summary>Parses a scope name from a command line, or null when it is not one.</summary>
    /// <param name="value">The name as typed.</param>
    public static RepairPolicy? Parse(string value) =>
        Enum.TryParse(value, ignoreCase: true, out RepairScope scope) ? new RepairPolicy(scope) : null;
}
