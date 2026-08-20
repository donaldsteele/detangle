using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Graph;

/// <summary>One inbound link to a document.</summary>
/// <param name="Source">The document the link was written in.</param>
/// <param name="Resolution">The chain's answer for that link.</param>
public sealed record Backlink(VaultDocument Source, LinkResolution Resolution)
{
    /// <summary>1-based line of the link in its source document.</summary>
    public int Line => Resolution.Link.Line;
}

/// <summary>
/// A page that names another page without linking to it — an unlinked mention
/// (plan.md section 6.1).
/// </summary>
/// <param name="Source">The document the mention appears in.</param>
/// <param name="MatchedText">The alias or title that was matched.</param>
/// <param name="Line">1-based line of the mention.</param>
/// <param name="Context">The line it appears on, for the preview.</param>
public sealed record UnlinkedMention(VaultDocument Source, string MatchedText, int Line, string Context);

/// <summary>
/// The vault's link graph: who points at whom, resolved.
/// <para>
/// Built once per scan from resolutions rather than from raw link text, so a backlink
/// panel shows the pages that actually reach this one — including the ones that reached
/// it through a normalized or alias match, which is exactly the set every other viewer
/// loses.
/// </para>
/// </summary>
public sealed class LinkGraph
{
    private readonly Dictionary<string, List<Backlink>> _inbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LinkResolution>> _outbound = new(StringComparer.Ordinal);

    private LinkGraph(VaultSnapshot vault)
    {
        Vault = vault;
    }

    /// <summary>The vault this graph was built over.</summary>
    public VaultSnapshot Vault { get; }

    /// <summary>Every resolution in the vault, in document order.</summary>
    public IReadOnlyList<LinkResolution> Resolutions { get; private set; } = [];

    /// <summary>Builds the graph by resolving every link in every document.</summary>
    public static LinkGraph Build(VaultSnapshot vault, IReadOnlyDictionary<string, string>? rememberedChoices = null)
    {
        var graph = new LinkGraph(vault);
        LinkResolver resolver = vault.CreateResolver(rememberedChoices);
        var all = new List<LinkResolution>();

        foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
        {
            var outbound = new List<LinkResolution>();

            foreach (LinkResolution resolution in resolver.ResolveAll(document))
            {
                all.Add(resolution);
                outbound.Add(resolution);

                if (resolution.Target is not { } target
                    || string.Equals(target.RelativePath, document.RelativePath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!graph._inbound.TryGetValue(target.RelativePath, out List<Backlink>? backlinks))
                {
                    backlinks = [];
                    graph._inbound[target.RelativePath] = backlinks;
                }

                backlinks.Add(new Backlink(document, resolution));
            }

            graph._outbound[document.RelativePath] = outbound;
        }

        graph.Resolutions = all;

        return graph;
    }

    /// <summary>Documents that link to this one, with the resolution that got them there.</summary>
    public IReadOnlyList<Backlink> BacklinksTo(VaultDocument document) =>
        _inbound.TryGetValue(document.RelativePath, out List<Backlink>? backlinks) ? backlinks : [];

    /// <summary>Links written in this document.</summary>
    public IReadOnlyList<LinkResolution> OutboundFrom(VaultDocument document) =>
        _outbound.TryGetValue(document.RelativePath, out List<LinkResolution>? outbound) ? outbound : [];

    /// <summary>How many documents link to this one.</summary>
    public int InboundCount(VaultDocument document) => BacklinksTo(document).Count;

    /// <summary>Documents nothing links to.</summary>
    public IEnumerable<VaultDocument> Orphans() =>
        Vault.Documents.Where(d => d.IsMarkdown && InboundCount(d) == 0);

    /// <summary>
    /// Pages that name this one — by title, alias or stem — without linking to it. The
    /// search is over rendered text lines rather than the raw file so that a mention
    /// inside a code fence is not offered as a link the reader should make.
    /// </summary>
    /// <param name="document">The document to find mentions of.</param>
    /// <param name="contentReader">Reads a document's text.</param>
    /// <param name="limit">Maximum mentions to return.</param>
    public IReadOnlyList<UnlinkedMention> UnlinkedMentions(
        VaultDocument document, Func<VaultDocument, string?> contentReader, int limit = 50)
    {
        List<string> names = [.. Names(document).Where(n => n.Length >= 4).Distinct(StringComparer.OrdinalIgnoreCase)];

        if (names.Count == 0)
        {
            return [];
        }

        var mentions = new List<UnlinkedMention>();
        HashSet<string> linkers = [.. BacklinksTo(document).Select(b => b.Source.RelativePath)];

        foreach (VaultDocument candidate in Vault.Documents.Where(d => d.IsMarkdown))
        {
            if (mentions.Count >= limit)
            {
                break;
            }

            if (string.Equals(candidate.RelativePath, document.RelativePath, StringComparison.Ordinal)
                || linkers.Contains(candidate.RelativePath))
            {
                continue;
            }

            string? content = contentReader(candidate);

            if (content is null)
            {
                continue;
            }

            string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            bool inFence = false;

            for (int i = 0; i < lines.Length && mentions.Count < limit; i++)
            {
                string line = lines[i];

                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence)
                {
                    continue;
                }

                foreach (string name in names)
                {
                    int index = line.IndexOf(name, StringComparison.OrdinalIgnoreCase);

                    if (index < 0 || IsInsideLink(line, index))
                    {
                        continue;
                    }

                    mentions.Add(new UnlinkedMention(candidate, name, i + 1, line.Trim()));
                    break;
                }
            }
        }

        return mentions;
    }

    /// <summary>The names a document can be mentioned by.</summary>
    private static IEnumerable<string> Names(VaultDocument document)
    {
        if (document.Frontmatter.Title is { Length: > 0 } title)
        {
            yield return title;
        }

        foreach (string alias in document.Frontmatter.Aliases)
        {
            yield return alias;
        }

        if (document.FirstHeading is { Length: > 0 } heading)
        {
            yield return heading;
        }

        yield return document.Stem;
    }

    /// <summary>
    /// True when a match sits inside link syntax already. A mention the author has
    /// written as "[[Page]]" or "[text](page.md)" is a link, not a missed one.
    /// </summary>
    private static bool IsInsideLink(string line, int index)
    {
        int openWiki = line.LastIndexOf("[[", index, StringComparison.Ordinal);
        int closeWiki = line.LastIndexOf("]]", index, StringComparison.Ordinal);

        if (openWiki >= 0 && openWiki > closeWiki)
        {
            return true;
        }

        int openParen = line.LastIndexOf('(', index);
        int closeParen = line.LastIndexOf(')', index);

        return openParen > 0 && openParen > closeParen && line[openParen - 1] == ']';
    }
}
