using System.Text.Json;

namespace Detangle.Core.History;

/// <summary>
/// A <see cref="VaultDelta"/> written into a findings report (plan.md section 15.4).
/// <para>
/// The vocabulary here — broke, healed, degraded, improved, retargeted — is the same one
/// the panel's change summary uses, deliberately. A gate in continuous integration and the
/// application the person who wrote the wiki is looking at have to describe the same
/// regression the same way, or the conversation about it starts with a translation.
/// </para>
/// </summary>
public static class DeltaReport
{
    /// <summary>Writes the delta into an open JSON object, under its own property.</summary>
    /// <param name="delta">What changed since the baseline.</param>
    /// <param name="writer">The writer, positioned inside an object.</param>
    /// <param name="propertyName">The property to write it as.</param>
    public static void Write(this VaultDelta delta, Utf8JsonWriter writer, string propertyName = "delta")
    {
        writer.WriteStartObject(propertyName);

        writer.WriteStartObject("pages");
        writer.WriteNumber("added", delta.Added.Count);
        writer.WriteNumber("removed", delta.Removed.Count);
        writer.WriteNumber("renamed", delta.Renamed.Count);
        writer.WriteNumber("rewritten", delta.Rewritten.Count);
        writer.WriteEndObject();

        writer.WriteStartObject("links");

        foreach (LinkChangeKind kind in Enum.GetValues<LinkChangeKind>())
        {
            writer.WriteNumber(Name(kind), delta.Links.Count(l => l.Kind == kind));
        }

        writer.WriteEndObject();

        writer.WriteBoolean("regressed", delta.HasRegression);

        // Only the links that got worse are listed. The healed and improved ones are
        // counted above, and a report that listed every link that moved in either
        // direction would bury the ones somebody has to act on.
        writer.WriteStartArray("regressions");

        foreach (LinkChange change in delta.Regressions)
        {
            writer.WriteStartObject();
            writer.WriteString("source", change.SourcePath);
            writer.WriteString("target", change.RawTarget);
            writer.WriteString("change", Name(change.Kind));
            writer.WriteString("was", change.Before.Rule.ToString());
            writer.WriteString("now", change.After.Rule.ToString());
            writer.WriteString("resolvedTo", change.After.TargetPath);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// What each change is called in the report and in the panel. "Fixed" is written as
    /// "healed" because a report also carries findings that a reader fixes, and the two
    /// senses of the word in one document is one too many.
    /// </summary>
    public static string Name(LinkChangeKind kind) => kind switch
    {
        LinkChangeKind.Broke => "broke",
        LinkChangeKind.Fixed => "healed",
        LinkChangeKind.Degraded => "degraded",
        LinkChangeKind.Improved => "improved",
        _ => "retargeted",
    };
}
