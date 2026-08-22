using System.Collections.ObjectModel;
using Detangle.Core.Diagnostics;

namespace Detangle.App;

/// <summary>
/// One kind of finding and every instance of it, for the triage tree (plan.md section
/// 15.3).
/// <para>
/// Grouping is hand-rolled because Avalonia has no <c>ICollectionView</c>: a flat list of
/// four hundred findings is a list nobody finishes, and "eleven broken anchors" is a
/// sentence a reader can decide about before opening any of them.
/// </para>
/// </summary>
public sealed class FindingGroup
{
    /// <summary>Creates a group.</summary>
    /// <param name="kind">The kind every finding in it shares.</param>
    /// <param name="findings">The findings, in the order they will be shown.</param>
    public FindingGroup(FindingKind kind, IEnumerable<Finding> findings)
    {
        Kind = kind;
        Findings = [.. findings];
        Severity = Findings.Count > 0 ? Findings.Min(f => f.Severity) : FindingSeverity.Info;
    }

    /// <summary>The kind of problem.</summary>
    public FindingKind Kind { get; }

    /// <summary>The worst severity in the group, which is the one worth showing on it.</summary>
    public FindingSeverity Severity { get; }

    /// <summary>The findings.</summary>
    public ObservableCollection<Finding> Findings { get; }

    /// <summary>The group's heading: what it is and how many there are.</summary>
    public string Title => $"{Readable(Kind)} · {Findings.Count}";

    /// <summary>
    /// The kind's name with the words separated, because "AnchorDialectDrift" is a type
    /// name and "Anchor dialect drift" is what the panel is telling someone.
    /// </summary>
    private static string Readable(FindingKind kind)
    {
        string name = kind.ToString();
        var text = new System.Text.StringBuilder(name.Length + 4);

        foreach (char character in name)
        {
            if (char.IsUpper(character) && text.Length > 0)
            {
                text.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                text.Append(character);
            }
        }

        return text.ToString();
    }
}
