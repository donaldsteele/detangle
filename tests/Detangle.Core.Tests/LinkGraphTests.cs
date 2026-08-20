using Detangle.Core.Graph;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for backlinks, orphans and unlinked mentions.</summary>
public class LinkGraphTests
{
    [Fact]
    public void BacklinksIncludeLinksThatOnlyResolvedAfterNormalization()
    {
        // This is the whole point: "[[My Target]]" against "my-target.md" is a backlink
        // every viewer that matches on raw text loses.
        LinkGraph graph = BuildGraph(
            ("source.md", "[[My Target]]"),
            ("my-target.md", "# My Target"));

        Backlink backlink = Assert.Single(graph.BacklinksTo(Document(graph, "my-target.md")));

        Assert.Equal("source.md", backlink.Source.RelativePath);
        Assert.Equal(1, backlink.Line);
    }

    [Fact]
    public void ADocumentDoesNotBacklinkToItself()
    {
        LinkGraph graph = BuildGraph(("page.md", "# Page\n\n[[#Page]] and [[page]]\n"));

        Assert.Empty(graph.BacklinksTo(Document(graph, "page.md")));
    }

    [Fact]
    public void CountsInboundLinksPerDocument()
    {
        LinkGraph graph = BuildGraph(
            ("a.md", "[[target]]"),
            ("b.md", "[[target]]"),
            ("target.md", "# Target"));

        Assert.Equal(2, graph.InboundCount(Document(graph, "target.md")));
    }

    [Fact]
    public void OrphansAreDocumentsNothingPointsAt()
    {
        LinkGraph graph = BuildGraph(
            ("hub.md", "[[spoke]]"),
            ("spoke.md", "# Spoke"),
            ("lonely.md", "# Lonely"));

        Assert.Equal(
            ["hub.md", "lonely.md"],
            graph.Orphans().Select(d => d.RelativePath).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void OutboundLinksAreKeptPerDocument()
    {
        LinkGraph graph = BuildGraph(
            ("source.md", "[[a]] and [[b]] and [[nowhere]]"),
            ("a.md", "# A"),
            ("b.md", "# B"));

        Assert.Equal(3, graph.OutboundFrom(Document(graph, "source.md")).Count);
    }

    [Fact]
    public void UnlinkedMentionsFindPagesThatNameATargetWithoutLinkingIt()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target.md"] = "---\ntitle: Attention Mechanism\n---\n\n# Attention Mechanism\n",
            ["mentions.md"] = "The Attention Mechanism is discussed here.\n",
            ["links.md"] = "See [[Attention Mechanism]].\n",
        };

        LinkGraph graph = BuildGraph([.. files.Select(f => (f.Key, f.Value))]);

        UnlinkedMention mention = Assert.Single(
            graph.UnlinkedMentions(Document(graph, "target.md"), d => files[d.RelativePath]));

        Assert.Equal("mentions.md", mention.Source.RelativePath);
        Assert.Equal("Attention Mechanism", mention.MatchedText);
        Assert.Equal(1, mention.Line);
    }

    [Fact]
    public void UnlinkedMentionsIgnoreCodeFences()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target.md"] = "# Attention Mechanism\n",
            ["other.md"] = "```python\n# Attention Mechanism goes here\n```\n",
        };

        LinkGraph graph = BuildGraph([.. files.Select(f => (f.Key, f.Value))]);

        Assert.Empty(graph.UnlinkedMentions(Document(graph, "target.md"), d => files[d.RelativePath]));
    }

    [Fact]
    public void UnlinkedMentionsIgnoreTextAlreadyInsideALink()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target.md"] = "# Attention Mechanism\n",
            ["other.md"] = "See [[Attention Mechanism|the mechanism]] for details.\n",
        };

        LinkGraph graph = BuildGraph([.. files.Select(f => (f.Key, f.Value))]);

        Assert.Empty(graph.UnlinkedMentions(Document(graph, "target.md"), d => files[d.RelativePath]));
    }

    [Fact]
    public void ShortNamesAreNotOfferedAsMentions()
    {
        // A two-letter stem would match half the vault; the noise would drown the signal.
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ai.md"] = "# AI\n",
            ["other.md"] = "This mentions ai and AI repeatedly.\n",
        };

        LinkGraph graph = BuildGraph([.. files.Select(f => (f.Key, f.Value))]);

        Assert.Empty(graph.UnlinkedMentions(Document(graph, "ai.md"), d => files[d.RelativePath]));
    }

    [Fact]
    public void BuildsOverARealFixtureVault()
    {
        VaultSnapshot vault = FixtureVaults.Load("llm-wiki");
        LinkGraph graph = LinkGraph.Build(vault);

        VaultDocument entity = vault.Index.ByRelativePath("wiki/entities/vaswani-ashish.md").Single();

        // Two wikilinks and one frontmatter "sources:" entry, which is a link the graph
        // counts even though it carries no brackets.
        Assert.Equal(3, graph.InboundCount(entity));
        Assert.Contains(
            graph.BacklinksTo(entity),
            b => b.Resolution.Link.Syntax == Core.Linking.LinkSyntax.Frontmatter);
    }

    private static LinkGraph BuildGraph(params (string Path, string Content)[] files)
    {
        var documents = files
            .Select(f => TestVault.CreateDocument(f.Path, f.Content))
            .ToList();

        var vault = new VaultSnapshot
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = Core.Linking.VaultIndex.Build(documents),
        };

        return LinkGraph.Build(vault);
    }

    private static VaultDocument Document(LinkGraph graph, string relativePath) =>
        graph.Vault.Index.ByRelativePath(relativePath).Single();
}
