using System.Security.Cryptography;
using System.Text;
using Detangle.Core.Diagnostics;
using Detangle.Core.Vault;

namespace Detangle.Core.Repair;

/// <summary>
/// Plans a repair across a whole vault, without touching it (plan.md section 6.3).
/// <para>
/// The rewrite is replayed in memory over each file once, so two links on one line do not
/// shift each other and no file is read more than necessary. Nothing here opens a file for
/// writing: the caller is handed a plan and decides.
/// </para>
/// </summary>
public static class VaultRepair
{
    /// <summary>Plans the repair a set of findings implies.</summary>
    /// <param name="findings">The Link Doctor's findings, already suggested.</param>
    /// <param name="contentReader">How to read a document's text; null skips the file.</param>
    /// <param name="policy">What counts as repairable.</param>
    public static PatchSet Plan(
        IEnumerable<Finding> findings,
        Func<VaultDocument, string?> contentReader,
        RepairPolicy? policy = null)
    {
        RepairPolicy inForce = policy ?? RepairPolicy.Safe;
        var patches = new List<FilePatch>();

        IEnumerable<IGrouping<string, Finding>> byDocument = findings
            .Where(inForce.Includes)
            .GroupBy(f => f.Document.RelativePath, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, Finding> group in byDocument)
        {
            VaultDocument document = group.First().Document;

            if (contentReader(document) is not { } original)
            {
                continue;
            }

            // Later lines first, and later columns first within a line: rewriting from
            // the bottom up keeps every remaining finding's line number valid, and from
            // the right keeps every remaining finding's column valid. A canonical target
            // is rarely the same length as what it replaces, so two links on one line
            // would otherwise shift each other.
            IReadOnlyList<Finding> ordered =
            [
                .. group
                    .OrderByDescending(f => f.Line)
                    .ThenByDescending(f => f.Resolution?.Link.Column ?? 0),
            ];

            string content = original;
            var applied = new List<Finding>();

            foreach (Finding finding in ordered)
            {
                if (LinkDoctor.ApplyRewrite(content, finding) is { } rewritten)
                {
                    applied.Add(finding);
                    content = rewritten;
                }
            }

            // One hunk per changed line, not per finding. Two links on one line are two
            // findings and one edit, and emitting two hunks that both claim that line
            // would give a patch which applies the first rewrite twice.
            string[] before = Lines(original);
            string[] after = Lines(content);
            var hunks = new List<Hunk>();

            foreach (IGrouping<int, Finding> line in applied.GroupBy(f => f.Line).OrderBy(g => g.Key))
            {
                int index = line.Key - 1;

                if (index < 0 || index >= before.Length || index >= after.Length
                    || string.Equals(before[index], after[index], StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<Finding> onLine = [.. line.OrderBy(f => f.Resolution?.Link.Column ?? 0)];

                hunks.Add(new Hunk(
                    line.Key,
                    before[index],
                    after[index],
                    string.Join(", ", onLine.Select(f => f.Resolution?.Rule.ToString() ?? "unknown")),
                    string.Join(", ", onLine.Select(f => f.Resolution?.Link.RawTarget ?? string.Empty)),
                    onLine.Count));
            }

            if (hunks.Count > 0)
            {
                patches.Add(new FilePatch(
                    document.RelativePath,
                    Hash(original),
                    original.Contains("\r\n", StringComparison.Ordinal),
                    hunks));
            }
        }

        return new PatchSet(patches, inForce);
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// The same hash the baseline uses, for the same reason: line endings and Unicode
    /// composition must not make an unchanged file look rewritten.
    /// </summary>
    private static string Hash(string content)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
