using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Graph;

/// <summary>One node of the tag hierarchy.</summary>
/// <param name="Segment">This level's name, without its parents.</param>
/// <param name="FullTag">The whole tag path, "a/b/c".</param>
/// <param name="Documents">Documents carrying exactly this tag.</param>
/// <param name="Children">Nested tags.</param>
public sealed record TagNode(
    string Segment,
    string FullTag,
    IReadOnlyList<VaultDocument> Documents,
    IReadOnlyList<TagNode> Children)
{
    private IReadOnlyList<VaultDocument>? _all;

    /// <summary>Documents on this tag and every tag under it.</summary>
    public int TotalCount => AllDocuments.Count;

    /// <summary>
    /// Every document on this tag or any tag under it, by title.
    /// <para>
    /// The tree is hierarchical, so selecting "llm" means "llm and everything below it" —
    /// the same rule <c>tag:llm</c> follows in search. A count in the rail and a count in
    /// the search box that disagreed would be a subtler version of the problem this list
    /// exists to fix, so both sides read the tag the same way.
    /// </para>
    /// <para>
    /// Computed once. Every row of the tag tree binds the count, and a tree of two hundred
    /// tags walking its own subtree on every measure is a rail that stutters while it
    /// scrolls. The node is immutable, so the answer cannot go stale.
    /// </para>
    /// </summary>
    public IReadOnlyList<VaultDocument> AllDocuments => _all ??= Gather();

    private IReadOnlyList<VaultDocument> Gather()
    {
        // A document carrying both "llm" and "llm/agents" appears once. Reference
        // identity is the right comparison: every document here came out of one scan.
        var seen = new HashSet<VaultDocument>();
        var all = new List<VaultDocument>();

        Collect(this);

        return [.. all.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

        void Collect(TagNode node)
        {
            foreach (VaultDocument document in node.Documents)
            {
                if (seen.Add(document))
                {
                    all.Add(document);
                }
            }

            foreach (TagNode child in node.Children)
            {
                Collect(child);
            }
        }
    }
}

/// <summary>
/// Builds the tag browser's tree (plan.md section 6.1).
/// <para>
/// Tags come from two places that vaults treat as one: the frontmatter "tags" key and
/// inline "#tag" text. Both are collected, and nested tags — "llm/agents" — become a
/// hierarchy, because a flat list of two hundred tags is not a browser.
/// </para>
/// </summary>
public static class TagTree
{
    /// <summary>Builds the tag tree for a vault.</summary>
    public static IReadOnlyList<TagNode> Build(VaultSnapshot vault)
    {
        var byTag = new Dictionary<string, List<VaultDocument>>(StringComparer.OrdinalIgnoreCase);

        foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
        {
            foreach (string tag in TagsOf(document))
            {
                string cleaned = tag.Trim().Trim('#', '/');

                if (cleaned.Length == 0)
                {
                    continue;
                }

                if (!byTag.TryGetValue(cleaned, out List<VaultDocument>? documents))
                {
                    documents = [];
                    byTag[cleaned] = documents;
                }

                if (!documents.Contains(document))
                {
                    documents.Add(document);
                }
            }
        }

        return BuildLevel(byTag, prefix: string.Empty);
    }

    /// <summary>Every tag a document carries, from frontmatter and from its text.</summary>
    public static IEnumerable<string> TagsOf(VaultDocument document)
    {
        foreach (string tag in document.Frontmatter.Tags)
        {
            yield return tag;
        }

        foreach (LinkReference link in document.Links.Where(l => l.Syntax == LinkSyntax.Tag))
        {
            yield return link.RawTarget;
        }
    }

    private static IReadOnlyList<TagNode> BuildLevel(
        Dictionary<string, List<VaultDocument>> byTag, string prefix)
    {
        var segments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string tag in byTag.Keys)
        {
            if (prefix.Length > 0 && !tag.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string remainder = prefix.Length == 0 ? tag : tag[(prefix.Length + 1)..];
            string segment = remainder.Split('/')[0];

            segments[segment] = prefix.Length == 0 ? segment : $"{prefix}/{segment}";
        }

        var nodes = new List<TagNode>();

        foreach ((string segment, string fullTag) in segments.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            byTag.TryGetValue(fullTag, out List<VaultDocument>? documents);

            nodes.Add(new TagNode(
                segment,
                fullTag,
                documents ?? [],
                BuildLevel(byTag, fullTag)));
        }

        return nodes;
    }
}
