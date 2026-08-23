using Detangle.Core.Linking;

namespace Detangle.Core.History;

/// <summary>What happened to one link between two snapshots.</summary>
public enum LinkChangeKind
{
    /// <summary>It resolved before and resolves to nothing now.</summary>
    Broke,

    /// <summary>It resolved to nothing before and resolves now.</summary>
    Fixed,

    /// <summary>It still resolves, but only because a later step of the chain rescued it.</summary>
    Degraded,

    /// <summary>It still resolves, and now by an earlier step than before.</summary>
    Improved,

    /// <summary>It resolves by the same rule, to a different document.</summary>
    Retargeted,
}

/// <summary>One link that resolves differently than it did.</summary>
/// <param name="SourcePath">The document the link is written in.</param>
/// <param name="RawTarget">The target exactly as written.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Before">How it resolved at the baseline.</param>
/// <param name="After">How it resolves now.</param>
public sealed record LinkChange(
    string SourcePath,
    string RawTarget,
    LinkChangeKind Kind,
    LinkRecord Before,
    LinkRecord After);

/// <summary>
/// The difference between a marked baseline and the vault as it stands (plan.md section
/// 15.4).
/// <para>
/// "Twelve links that resolved by ExactVaultPath now resolve by FuzzyNearest" is a
/// sentence no other tool in the category can produce, because every competitor's link is
/// binary — it works or it does not — so none of them can represent a link that still
/// works but works worse. The ladder is what makes a degradation visible at all.
/// </para>
/// </summary>
/// <param name="Added">Documents that were not there before.</param>
/// <param name="Removed">Documents that are gone, excluding the ones that were renamed.</param>
/// <param name="Renamed">Documents that moved, old path to new.</param>
/// <param name="Rewritten">Documents whose text changed.</param>
/// <param name="Links">Links that resolve differently.</param>
public sealed record VaultDelta(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyDictionary<string, string> Renamed,
    IReadOnlyList<string> Rewritten,
    IReadOnlyList<LinkChange> Links)
{
    /// <summary>Nothing changed.</summary>
    public static VaultDelta None { get; } = new([], [], new Dictionary<string, string>(StringComparer.Ordinal), [], []);

    /// <summary>True when the two snapshots are the same vault in the same state.</summary>
    public bool IsEmpty =>
        Added.Count == 0 && Removed.Count == 0 && Renamed.Count == 0
        && Rewritten.Count == 0 && Links.Count == 0;

    /// <summary>Every document touched, which is what a "since last run" filter needs.</summary>
    public IReadOnlySet<string> TouchedDocuments
    {
        get
        {
            var touched = new HashSet<string>(StringComparer.Ordinal);

            touched.UnionWith(Added);
            touched.UnionWith(Rewritten);
            touched.UnionWith(Renamed.Values);

            foreach (LinkChange change in Links)
            {
                touched.Add(change.SourcePath);
            }

            return touched;
        }
    }

    /// <summary>One line a reader can act on, or null when nothing changed.</summary>
    public string? Summary()
    {
        if (IsEmpty)
        {
            return null;
        }

        var parts = new List<string>(7);

        Add(Added.Count, "new page", "new pages");
        Add(Rewritten.Count, "rewritten page", "rewritten pages");
        Add(Renamed.Count, "renamed page", "renamed pages");
        Add(Removed.Count, "removed page", "removed pages");
        Add(Links.Count(l => l.Kind == LinkChangeKind.Broke), "link broke", "links broke");
        Add(
            Links.Count(l => l.Kind == LinkChangeKind.Degraded),
            "link now needs a later rule",
            "links now need a later rule");
        Add(Links.Count(l => l.Kind == LinkChangeKind.Fixed), "link fixed", "links fixed");

        return parts.Count == 0 ? null : string.Join(" \u00b7 ", parts);

        void Add(int count, string one, string many)
        {
            if (count > 0)
            {
                parts.Add($"{count} {(count == 1 ? one : many)}");
            }
        }
    }

    /// <summary>
    /// True when something resolves worse than it did: a link that broke, or one that now
    /// needs a later rung of the chain than it used to.
    /// <para>
    /// This is the question a gate in continuous integration has to ask. "Are there
    /// errors" is the wrong one for a corpus a generator rewrites wholesale — an
    /// already-broken wiki fails that test on every run, so nobody can tell the run that
    /// made it worse from the twenty that did not.
    /// </para>
    /// </summary>
    public bool HasRegression => Links.Any(
        l => l.Kind is LinkChangeKind.Broke or LinkChangeKind.Degraded);

    /// <summary>The links that got worse, worst first.</summary>
    public IEnumerable<LinkChange> Regressions => Links
        .Where(l => l.Kind is LinkChangeKind.Broke or LinkChangeKind.Degraded)
        .OrderBy(l => l.Kind);

    /// <summary>Compares a baseline with the vault as it stands.</summary>
    /// <param name="previous">The marked baseline.</param>
    /// <param name="current">A record taken now.</param>
    public static VaultDelta Compare(VaultSnapshotRecord previous, VaultSnapshotRecord current)
    {
        if (previous.Documents.Count == 0 && previous.Links.Count == 0)
        {
            // No baseline is not "everything is new"; it is "there is nothing to compare
            // against", and reporting a whole vault as added would be noise on first run.
            return None;
        }

        var added = new List<string>();
        var rewritten = new List<string>();

        foreach (DocumentRecord document in current.Documents.Values)
        {
            if (previous.Documents.TryGetValue(document.RelativePath, out DocumentRecord? before))
            {
                if (!string.Equals(before.ContentHash, document.ContentHash, StringComparison.Ordinal))
                {
                    rewritten.Add(document.RelativePath);
                }
            }
            else
            {
                added.Add(document.RelativePath);
            }
        }

        var removed = previous.Documents.Keys.Where(p => !current.Documents.ContainsKey(p)).ToList();

        // A rename is a removal and an addition that share an identity. Matching on
        // frontmatter id first, then the normalized stem, is what keeps a moved page from
        // being reported as one page lost and another gained.
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string gone in removed.ToList())
        {
            string identity = previous.Documents[gone].Identity;

            string? arrival = added.FirstOrDefault(
                p => string.Equals(current.Documents[p].Identity, identity, StringComparison.Ordinal));

            if (arrival is not null)
            {
                renamed[gone] = arrival;
                removed.Remove(gone);
                added.Remove(arrival);
            }
        }

        var links = new List<LinkChange>();

        foreach ((string key, LinkRecord after) in current.Links)
        {
            if (!previous.Links.TryGetValue(key, out LinkRecord? before) || Same(before, after))
            {
                continue;
            }

            links.Add(new LinkChange(after.SourcePath, after.RawTarget, Classify(before, after), before, after));
        }

        return new VaultDelta(
            [.. added.Order(StringComparer.Ordinal)],
            [.. removed.Order(StringComparer.Ordinal)],
            renamed,
            [.. rewritten.Order(StringComparer.Ordinal)],
            [.. links
                .OrderBy(l => l.Kind)
                .ThenBy(l => l.SourcePath, StringComparer.Ordinal)]);
    }

    private static bool Same(LinkRecord before, LinkRecord after) =>
        before.Rule == after.Rule
        && string.Equals(before.TargetPath, after.TargetPath, StringComparison.Ordinal);

    /// <summary>
    /// Which way a link moved. The rules are ordered by how much the chain had to do, so
    /// a higher rule is a worse answer to the same question — which is the whole reason
    /// this comparison can say anything the competition cannot.
    /// </summary>
    private static LinkChangeKind Classify(LinkRecord before, LinkRecord after)
    {
        bool resolvedBefore = before.TargetPath.Length > 0;
        bool resolvedAfter = after.TargetPath.Length > 0;

        if (resolvedBefore && !resolvedAfter)
        {
            return LinkChangeKind.Broke;
        }

        if (!resolvedBefore && resolvedAfter)
        {
            return LinkChangeKind.Fixed;
        }

        if (after.Rule > before.Rule)
        {
            return LinkChangeKind.Degraded;
        }

        return after.Rule < before.Rule ? LinkChangeKind.Improved : LinkChangeKind.Retargeted;
    }
}
