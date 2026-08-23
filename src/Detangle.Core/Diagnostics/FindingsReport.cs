using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Detangle.Core.Graph;
using Detangle.Core.History;
using Detangle.Core.Linking;

namespace Detangle.Core.Diagnostics;

/// <summary>
/// The Link Doctor's findings, written for a machine to read (plan.md section 15.5).
/// <para>
/// The payload that matters here is not the broken links. Every link checker has reported
/// those for years, and several of them already emit JSON. It is the histogram: how many
/// links in each folder needed which step of the chain to resolve. "Every link in raw/
/// needed step 10, encoding-variant rescue" is a fact only a resolver with a ladder can
/// state, and it is feedback the generator that wrote the wiki can act on.
/// </para>
/// <para>
/// Written with <see cref="Utf8JsonWriter"/> rather than the reflection-based serializer:
/// the browser head publishes at <c>TrimMode=full</c>, where reflection-based
/// serialization fails the build, and this type is shared with it.
/// </para>
/// </summary>
public static class FindingsReport
{
    /// <summary>The report's schema version, so a consumer can tell what it is reading.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes the report as JSON.</summary>
    /// <param name="graph">The resolved link graph the findings came from.</param>
    /// <param name="findings">The findings to report.</param>
    /// <param name="rootPath">The vault root, echoed back so a report identifies itself.</param>
    /// <param name="indented">False for one line, which is what a pipe wants.</param>
    /// <param name="delta">
    /// What changed since a marked baseline, when one was asked for. Absent rather than
    /// empty when no baseline was named: "nothing regressed" and "nothing was compared"
    /// are different answers, and a gate reading this has to tell them apart.
    /// </param>
    public static string Write(
        LinkGraph graph,
        IReadOnlyList<Finding> findings,
        string rootPath,
        bool indented = true,
        VaultDelta? delta = null)
    {
        using var stream = new MemoryStream();

        // The relaxed encoder is deliberate: the default escapes quotes and every
        // non-ASCII character to \uXXXX, which turns a report a person may well read into
        // noise. This output is a file and a pipe, never markup embedded in a page, so the
        // HTML escaping the strict encoder exists to provide buys nothing here.
        var options = new JsonWriterOptions
        {
            Indented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", SchemaVersion);
            writer.WriteString("vault", rootPath);
            writer.WriteString("flavor", graph.Vault.Profile.Flavor.ToString());
            writer.WriteNumber("documents", graph.Vault.Documents.Count);
            writer.WriteNumber("links", graph.Resolutions.Count);

            WriteCounts(writer, findings);
            WriteRules(writer, graph);

            delta?.Write(writer);

            WriteFindings(writer, findings);

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The worst severity in a set of findings, or null when there are none. What a
    /// caller compares against its own threshold to decide an exit code.
    /// </summary>
    public static FindingSeverity? WorstSeverity(IReadOnlyList<Finding> findings) =>
        findings.Count == 0 ? null : findings.Min(f => f.Severity);

    private static void WriteCounts(Utf8JsonWriter writer, IReadOnlyList<Finding> findings)
    {
        writer.WriteStartObject("counts");

        foreach (FindingSeverity severity in Enum.GetValues<FindingSeverity>())
        {
            writer.WriteNumber(Camel(severity.ToString()), findings.Count(f => f.Severity == severity));
        }

        writer.WriteStartObject("byKind");

        foreach (IGrouping<FindingKind, Finding> group in findings
            .GroupBy(f => f.Kind)
            .OrderBy(g => g.Key))
        {
            writer.WriteNumber(Camel(group.Key.ToString()), group.Count());
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Which chain step resolved how many links, per folder. This is the section the
    /// generator reads: a folder whose links all needed a late rule is a folder whose
    /// naming convention does not match the vault it was written into.
    /// </summary>
    private static void WriteRules(Utf8JsonWriter writer, LinkGraph graph)
    {
        writer.WriteStartObject("rules");

        foreach (IGrouping<string, LinkResolution> folder in graph.Resolutions
            .Where(r => r.Rule != ResolutionRule.NotAttempted)
            .GroupBy(r => Folder(r.Link.SourcePath))
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(folder.Key.Length == 0 ? "." : folder.Key);

            foreach (IGrouping<ResolutionRule, LinkResolution> rule in folder
                .GroupBy(r => r.Rule)
                .OrderBy(g => g.Key))
            {
                writer.WriteNumber(Camel(rule.Key.ToString()), rule.Count());
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteFindings(Utf8JsonWriter writer, IReadOnlyList<Finding> findings)
    {
        writer.WriteStartArray("findings");

        foreach (Finding finding in findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Document.RelativePath, StringComparer.Ordinal)
            .ThenBy(f => f.Line))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", Camel(finding.Kind.ToString()));
            writer.WriteString("severity", Camel(finding.Severity.ToString()));
            writer.WriteString("path", finding.Document.RelativePath);

            if (finding.Line > 0)
            {
                writer.WriteNumber("line", finding.Line);
            }

            writer.WriteString("message", finding.Message);

            if (finding.Resolution is { } resolution)
            {
                writer.WriteString("target", resolution.Link.RawTarget);
                writer.WriteString("rule", Camel(resolution.Rule.ToString()));

                if (resolution.Link.Anchor is { Length: > 0 } anchor)
                {
                    writer.WriteString("anchor", anchor);
                }
            }

            if (finding.SuggestedRewrite is { Length: > 0 } rewrite)
            {
                writer.WriteString("suggestedRewrite", rewrite);
            }

            if (finding.SuggestedAnchor is { Length: > 0 } heading)
            {
                writer.WriteString("suggestedAnchor", heading);
            }

            if (finding.Related.Count > 0)
            {
                writer.WriteStartArray("related");

                foreach (var document in finding.Related)
                {
                    writer.WriteStringValue(document.RelativePath);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string Folder(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');

        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    /// <summary>
    /// "BrokenAnchor" as "brokenAnchor". JSON keys read as data rather than as .NET type
    /// names, and a consumer written in anything else will expect this shape.
    /// </summary>
    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
}
