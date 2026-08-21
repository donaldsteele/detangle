using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Graph;

/// <summary>What a node in the graph view stands for.</summary>
public enum GraphNodeKind
{
    /// <summary>A markdown file in the vault.</summary>
    Page,

    /// <summary>
    /// A link target that matches no file. Shown as a node rather than dropped: a wiki's
    /// missing pages are the shape of the work left to do, and every other viewer hides
    /// them (plan.md section 6.4).
    /// </summary>
    MissingTarget,

    /// <summary>A folder standing in for the pages inside it, above the LOD threshold.</summary>
    Cluster,
}

/// <summary>
/// One node of the graph view.
/// </summary>
/// <param name="Index">Position in <see cref="GraphModel.Nodes"/>; edges address nodes by it.</param>
/// <param name="Id">Stable identity — a relative path, a missing target's normalized text, or a folder.</param>
/// <param name="Label">What the reader sees.</param>
/// <param name="Kind">Page, missing target or cluster.</param>
/// <param name="Document">The file, for a page node.</param>
/// <param name="Type">Frontmatter type, which colours the node.</param>
/// <param name="Folder">Vault-relative directory, which the folder filter and clustering use.</param>
/// <param name="Tags">Tags on the document, which the tag filter uses.</param>
/// <param name="InboundCount">How many pages link here; this sizes the node.</param>
/// <param name="OutboundCount">How many links this page writes.</param>
/// <param name="Weight">Pages represented — one, except for a cluster.</param>
public sealed record GraphNode(
    int Index,
    string Id,
    string Label,
    GraphNodeKind Kind,
    VaultDocument? Document,
    string? Type,
    string Folder,
    IReadOnlyList<string> Tags,
    int InboundCount,
    int OutboundCount,
    int Weight = 1)
{
    /// <summary>True for a page nothing links to.</summary>
    public bool IsOrphan => Kind == GraphNodeKind.Page && InboundCount == 0;
}

/// <summary>
/// One edge, already deduplicated: a page that links to another five times is one edge
/// of weight five rather than five overlapping lines.
/// </summary>
/// <param name="Source">Index of the linking node.</param>
/// <param name="Target">Index of the linked node.</param>
/// <param name="Weight">How many links this edge stands for.</param>
/// <param name="IsBroken">True when the target is a missing page.</param>
public sealed record GraphEdge(int Source, int Target, int Weight, bool IsBroken);

/// <summary>Which pages and links the graph view shows (plan.md section 6.4).</summary>
public sealed record GraphOptions
{
    /// <summary>Show everything.</summary>
    public static GraphOptions Default { get; } = new();

    /// <summary>Keep only pages carrying one of these tags. Empty means no tag filter.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Keep only pages with one of these frontmatter types. Empty means no type filter.</summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>Keep only pages under this vault-relative folder. Null means no folder filter.</summary>
    public string? Folder { get; init; }

    /// <summary>Show nodes for link targets that match no file.</summary>
    public bool IncludeMissingTargets { get; init; } = true;

    /// <summary>Show pages nothing links to.</summary>
    public bool IncludeOrphans { get; init; } = true;

    /// <summary>Local-graph mode: the page to centre on. Null builds the whole vault.</summary>
    public VaultDocument? Focus { get; init; }

    /// <summary>How many link hops out from <see cref="Focus"/> to include.</summary>
    public int Hops { get; init; } = 2;
}

/// <summary>
/// The vault's link graph as nodes and edges, ready to lay out.
/// <para>
/// Built from resolutions rather than raw link text, so the picture agrees with the
/// backlinks pane: a page reached through a normalized or alias match is drawn as
/// connected, because it is.
/// </para>
/// </summary>
public sealed class GraphModel
{
    private GraphModel(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    /// <summary>An empty graph, for before a vault is open.</summary>
    public static GraphModel Empty { get; } = new([], []);

    /// <summary>The nodes, indexed by <see cref="GraphNode.Index"/>.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary>The edges, deduplicated and weighted.</summary>
    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>Builds the graph view's model from a resolved link graph.</summary>
    /// <param name="graph">The vault's link graph.</param>
    /// <param name="options">Filters and local-graph mode.</param>
    public static GraphModel Build(LinkGraph graph, GraphOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        GraphOptions settings = options ?? GraphOptions.Default;

        var pages = new List<PageEntry>();
        var byPath = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (VaultDocument document in graph.Vault.Documents.Where(d => d.IsMarkdown))
        {
            if (!Matches(document, settings))
            {
                continue;
            }

            byPath[document.RelativePath] = pages.Count;
            pages.Add(new PageEntry(document));
        }

        var missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<(int Source, int Target), EdgeEntry>();

        foreach (PageEntry page in pages)
        {
            foreach (LinkResolution resolution in graph.OutboundFrom(page.Document))
            {
                if (resolution.Link.IsExternal || resolution.Link.IsSelfReference)
                {
                    continue;
                }

                int source = byPath[page.Document.RelativePath];
                int? target = TargetOf(resolution, byPath, missing, pages.Count, settings);

                if (target is not { } index || index == source)
                {
                    continue;
                }

                bool broken = resolution.Target is null;
                var key = (source, index);

                edges[key] = edges.TryGetValue(key, out EdgeEntry? existing)
                    ? existing with { Weight = existing.Weight + 1 }
                    : new EdgeEntry(source, index, 1, broken);
            }
        }

        // Inbound counts come from the filtered edge set rather than from the whole vault:
        // in local-graph mode the reader is asking about this neighbourhood, and sizing a
        // node by links that are not on screen makes the picture unreadable.
        int[] inbound = new int[pages.Count + missing.Count];
        int[] outbound = new int[inbound.Length];

        foreach (EdgeEntry edge in edges.Values)
        {
            inbound[edge.Target] += edge.Weight;
            outbound[edge.Source] += edge.Weight;
        }

        var kept = Restrict(pages.Count + missing.Count, edges.Values, settings, byPath);

        return Assemble(pages, missing, edges.Values, inbound, outbound, kept, settings);
    }

    /// <summary>
    /// Collapses the graph to one node per top-level folder when it is too big to draw
    /// honestly. Above roughly fifteen hundred nodes the picture is a hairball whatever
    /// the layout does, and a hairball at ten frames a second is worse than a readable
    /// summary the reader can drill into (plan.md section 6.4).
    /// </summary>
    /// <param name="maxNodes">The node count above which folders are collapsed.</param>
    public GraphModel WithLevelOfDetail(int maxNodes = 1500)
    {
        if (maxNodes <= 0 || Nodes.Count <= maxNodes)
        {
            return this;
        }

        var clusters = new Dictionary<string, ClusterEntry>(StringComparer.OrdinalIgnoreCase);
        int[] mapping = new int[Nodes.Count];

        foreach (GraphNode node in Nodes)
        {
            string folder = TopLevelFolder(node);

            if (!clusters.TryGetValue(folder, out ClusterEntry? cluster))
            {
                cluster = new ClusterEntry(clusters.Count, folder);
                clusters[folder] = cluster;
            }

            cluster.Weight += node.Weight;
            cluster.Inbound += node.InboundCount;
            cluster.Outbound += node.OutboundCount;
            mapping[node.Index] = cluster.Index;
        }

        var merged = new Dictionary<(int, int), EdgeEntry>();

        foreach (GraphEdge edge in Edges)
        {
            int source = mapping[edge.Source];
            int target = mapping[edge.Target];

            if (source == target)
            {
                continue;
            }

            var key = (source, target);

            merged[key] = merged.TryGetValue(key, out EdgeEntry? existing)
                ? existing with { Weight = existing.Weight + edge.Weight }
                : new EdgeEntry(source, target, edge.Weight, false);
        }

        var nodes = clusters.Values
            .OrderBy(c => c.Index)
            .Select(c => new GraphNode(
                c.Index,
                c.Folder,
                c.Folder.Length == 0 ? "(root)" : c.Folder,
                GraphNodeKind.Cluster,
                Document: null,
                Type: null,
                Folder: c.Folder,
                Tags: [],
                InboundCount: c.Inbound,
                OutboundCount: c.Outbound,
                Weight: c.Weight))
            .ToList();

        return new GraphModel(
            nodes,
            [.. merged.Values.Select(e => new GraphEdge(e.Source, e.Target, e.Weight, e.IsBroken))]);
    }

    /// <summary>The node showing a document, if it is in the graph.</summary>
    public GraphNode? NodeFor(VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Nodes.FirstOrDefault(
            n => n.Kind == GraphNodeKind.Page
                && string.Equals(n.Id, document.RelativePath, StringComparison.Ordinal));
    }

    /// <summary>The distinct frontmatter types present, for the filter list and the palette.</summary>
    public IReadOnlyList<string> TypesPresent() =>
        [.. Nodes.Select(n => n.Type).Where(t => t is { Length: > 0 }).Distinct(StringComparer.OrdinalIgnoreCase)!];

    private static GraphModel Assemble(
        List<PageEntry> pages,
        Dictionary<string, int> missing,
        IEnumerable<EdgeEntry> edges,
        int[] inbound,
        int[] outbound,
        HashSet<int>? kept,
        GraphOptions settings)
    {
        var nodes = new List<GraphNode>(pages.Count + missing.Count);
        int[] remap = new int[inbound.Length];
        Array.Fill(remap, -1);

        for (int i = 0; i < pages.Count; i++)
        {
            if (kept is not null && !kept.Contains(i))
            {
                continue;
            }

            if (!settings.IncludeOrphans && inbound[i] == 0)
            {
                continue;
            }

            VaultDocument document = pages[i].Document;
            remap[i] = nodes.Count;

            nodes.Add(new GraphNode(
                nodes.Count,
                document.RelativePath,
                document.DisplayName,
                GraphNodeKind.Page,
                document,
                document.Frontmatter.Type,
                document.DirectoryPath,
                [.. TagTree.TagsOf(document)],
                inbound[i],
                outbound[i]));
        }

        foreach ((string target, int index) in missing.OrderBy(m => m.Value))
        {
            if (kept is not null && !kept.Contains(index))
            {
                continue;
            }

            remap[index] = nodes.Count;

            nodes.Add(new GraphNode(
                nodes.Count,
                target,
                target,
                GraphNodeKind.MissingTarget,
                Document: null,
                Type: null,
                Folder: string.Empty,
                Tags: [],
                inbound[index],
                OutboundCount: 0));
        }

        var mapped = new List<GraphEdge>();

        foreach (EdgeEntry edge in edges)
        {
            int source = remap[edge.Source];
            int target = remap[edge.Target];

            if (source >= 0 && target >= 0)
            {
                mapped.Add(new GraphEdge(source, target, edge.Weight, edge.IsBroken));
            }
        }

        return new GraphModel(nodes, mapped);
    }

    /// <summary>
    /// The node set local-graph mode keeps: everything within N hops of the focus,
    /// walked in both directions because a page's backlinks are as much its neighbourhood
    /// as its outbound links are.
    /// </summary>
    private static HashSet<int>? Restrict(
        int nodeCount,
        IEnumerable<EdgeEntry> edges,
        GraphOptions settings,
        Dictionary<string, int> byPath)
    {
        if (settings.Focus is null
            || !byPath.TryGetValue(settings.Focus.RelativePath, out int start))
        {
            return null;
        }

        var neighbours = new List<int>[nodeCount];

        foreach (EdgeEntry edge in edges)
        {
            (neighbours[edge.Source] ??= []).Add(edge.Target);
            (neighbours[edge.Target] ??= []).Add(edge.Source);
        }

        HashSet<int> reached = [start];
        List<int> frontier = [start];

        for (int hop = 0; hop < Math.Max(0, settings.Hops) && frontier.Count > 0; hop++)
        {
            var next = new List<int>();

            foreach (int node in frontier)
            {
                foreach (int neighbour in neighbours[node] ?? [])
                {
                    if (reached.Add(neighbour))
                    {
                        next.Add(neighbour);
                    }
                }
            }

            frontier = next;
        }

        return reached;
    }

    /// <summary>
    /// The node an outbound link points at, adding a missing-target node the first time
    /// an unresolvable target is seen. Targets are pooled by their normalized text, so
    /// twenty pages linking to the same absent page draw one node with twenty edges.
    /// </summary>
    private static int? TargetOf(
        LinkResolution resolution,
        Dictionary<string, int> byPath,
        Dictionary<string, int> missing,
        int pageCount,
        GraphOptions settings)
    {
        if (resolution.Target is { IsMarkdown: true } target)
        {
            return byPath.TryGetValue(target.RelativePath, out int index) ? index : null;
        }

        if (resolution.Target is not null || !resolution.IsUnresolved || !settings.IncludeMissingTargets)
        {
            return null;
        }

        string name = LinkNormalizer.Normalize(resolution.Link.RawTarget);

        if (name.Length == 0)
        {
            return null;
        }

        if (!missing.TryGetValue(name, out int existing))
        {
            existing = pageCount + missing.Count;
            missing[name] = existing;
        }

        return existing;
    }

    private static bool Matches(VaultDocument document, GraphOptions settings)
    {
        if (settings.Folder is { Length: > 0 } folder
            && !document.RelativePath.StartsWith(
                folder.EndsWith('/') ? folder : folder + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (settings.Types.Count > 0
            && (document.Frontmatter.Type is not { Length: > 0 } type
                || !settings.Types.Contains(type, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (settings.Tags.Count == 0)
        {
            return true;
        }

        // A tag filter matches a tag's descendants too: filtering to "llm" should keep a
        // page tagged "llm/attention", the same way the tag browser nests them.
        return TagTree.TagsOf(document).Any(
            tag => settings.Tags.Any(
                wanted => tag.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                    || tag.StartsWith(wanted + "/", StringComparison.OrdinalIgnoreCase)));
    }

    private static string TopLevelFolder(GraphNode node)
    {
        if (node.Kind == GraphNodeKind.MissingTarget)
        {
            return "(missing)";
        }

        string folder = node.Folder;
        int slash = folder.IndexOf('/', StringComparison.Ordinal);

        return slash < 0 ? folder : folder[..slash];
    }

    private sealed record PageEntry(VaultDocument Document);

    private sealed record EdgeEntry(int Source, int Target, int Weight, bool IsBroken);

    private sealed class ClusterEntry(int index, string folder)
    {
        public int Index { get; } = index;

        public string Folder { get; } = folder;

        public int Weight { get; set; }

        public int Inbound { get; set; }

        public int Outbound { get; set; }
    }
}
