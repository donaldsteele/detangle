using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the graph view's model (plan.md section 6.4) — what the picture is allowed
/// to show, and what it must not quietly drop.
/// </summary>
public class GraphModelTests
{
    [Fact]
    public void APageIsANodeAndALinkIsAnEdge()
    {
        GraphModel model = Build(("a.md", "[[b]]\n"), ("b.md", "# B\n"));

        Assert.Equal(2, model.Nodes.Count);
        GraphEdge edge = Assert.Single(model.Edges);

        Assert.Equal("a.md", model.Nodes[edge.Source].Id);
        Assert.Equal("b.md", model.Nodes[edge.Target].Id);
        Assert.False(edge.IsBroken);
    }

    [Fact]
    public void RepeatedLinksAreOneWeightedEdgeRatherThanFiveOverlappingLines()
    {
        GraphModel model = Build(("a.md", "[[b]] [[b]] [[b]]\n"), ("b.md", "# B\n"));

        Assert.Equal(3, Assert.Single(model.Edges).Weight);
    }

    [Fact]
    public void ALinkResolvedByANonExactRuleStillDrawsAnEdge()
    {
        // The whole product exists for this: "My Target" reaches my-target.md, so the
        // picture has to show them connected.
        GraphModel model = Build(("a.md", "[[My Target]]\n"), ("notes/my-target.md", "# My Target\n"));

        GraphEdge edge = Assert.Single(model.Edges);

        Assert.Equal("notes/my-target.md", model.Nodes[edge.Target].Id);
    }

    [Fact]
    public void AMissingTargetIsItsOwnNode()
    {
        GraphModel model = Build(("a.md", "[[nowhere]]\n"));

        GraphNode missing = Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);

        Assert.Equal("nowhere", missing.Id);
        Assert.True(Assert.Single(model.Edges).IsBroken);
    }

    [Fact]
    public void EveryPageLinkingToTheSameMissingTargetSharesOneNode()
    {
        GraphModel model = Build(
            ("a.md", "[[nowhere]]\n"), ("b.md", "[[Nowhere]]\n"), ("c.md", "[[nowhere.md]]\n"));

        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);
        Assert.Equal(3, model.Edges.Count);
    }

    [Fact]
    public void MissingTargetsCanBeTurnedOff()
    {
        GraphModel model = Build(
            new GraphOptions { IncludeMissingTargets = false }, ("a.md", "[[nowhere]]\n"));

        Assert.Empty(model.Edges);
        Assert.DoesNotContain(model.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);
    }

    [Fact]
    public void ExternalLinksAreNotNodes()
    {
        GraphModel model = Build(("a.md", "[docs](https://example.com/docs)\n"));

        Assert.Single(model.Nodes);
        Assert.Empty(model.Edges);
    }

    [Fact]
    public void ASelfLinkIsNotAnEdge()
    {
        Assert.Empty(Build(("a.md", "[[a]] and [[#Heading]]\n\n# Heading\n")).Edges);
    }

    [Fact]
    public void NodeSizeComesFromInboundLinks()
    {
        GraphModel model = Build(
            ("a.md", "[[hub]]\n"), ("b.md", "[[hub]]\n"), ("hub.md", "# Hub\n"));

        Assert.Equal(2, Assert.Single(model.Nodes, n => n.Id == "hub.md").InboundCount);
    }

    [Fact]
    public void AnOrphanIsMarkedAsOne()
    {
        GraphModel model = Build(("hub.md", "[[spoke]]\n"), ("spoke.md", "# Spoke\n"));

        Assert.True(Assert.Single(model.Nodes, n => n.Id == "hub.md").IsOrphan);
        Assert.False(Assert.Single(model.Nodes, n => n.Id == "spoke.md").IsOrphan);
    }

    [Fact]
    public void OrphansCanBeHidden()
    {
        GraphModel model = Build(
            new GraphOptions { IncludeOrphans = false },
            ("hub.md", "[[spoke]]\n"),
            ("spoke.md", "# Spoke\n"),
            ("lonely.md", "# Lonely\n"));

        Assert.DoesNotContain(model.Nodes, n => n.Id == "lonely.md");
    }

    [Fact]
    public void TheTypeFilterKeepsOnlyThatType()
    {
        GraphModel model = Build(
            new GraphOptions { Types = ["concept"] },
            ("a.md", "---\ntype: concept\n---\n\n[[b]]\n"),
            ("b.md", "---\ntype: paper\n---\n\n# B\n"));

        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.Page);
        Assert.Empty(model.Edges);
    }

    [Fact]
    public void TheTagFilterMatchesNestedTags()
    {
        GraphModel model = Build(
            new GraphOptions { Tags = ["llm"] },
            ("a.md", "---\ntags: [llm/attention]\n---\n\n# A\n"),
            ("b.md", "---\ntags: [cooking]\n---\n\n# B\n"));

        Assert.Equal("a.md", Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.Page).Id);
    }

    [Fact]
    public void TheFolderFilterKeepsOnlyThatSubtree()
    {
        GraphModel model = Build(
            new GraphOptions { Folder = "papers" },
            ("papers/a.md", "# A\n"),
            ("notes/b.md", "# B\n"));

        Assert.Equal("papers/a.md", Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.Page).Id);
    }

    [Fact]
    public void LocalModeKeepsTheNeighbourhoodAndDropsTheRest()
    {
        VaultSnapshot vault = Vault(
            ("centre.md", "[[one]]\n"),
            ("one.md", "[[two]]\n"),
            ("two.md", "[[three]]\n"),
            ("three.md", "# Three\n"),
            ("elsewhere.md", "# Elsewhere\n"));

        VaultDocument centre = vault.Index.ByRelativePath("centre.md").Single();

        GraphModel model = GraphModel.Build(
            LinkGraph.Build(vault), new GraphOptions { Focus = centre, Hops = 2 });

        Assert.Contains(model.Nodes, n => n.Id == "one.md");
        Assert.Contains(model.Nodes, n => n.Id == "two.md");
        Assert.DoesNotContain(model.Nodes, n => n.Id == "three.md");
        Assert.DoesNotContain(model.Nodes, n => n.Id == "elsewhere.md");
    }

    [Fact]
    public void LocalModeWalksBacklinksAsWellAsOutboundLinks()
    {
        VaultSnapshot vault = Vault(("inbound.md", "[[centre]]\n"), ("centre.md", "# Centre\n"));

        VaultDocument centre = vault.Index.ByRelativePath("centre.md").Single();

        GraphModel model = GraphModel.Build(
            LinkGraph.Build(vault), new GraphOptions { Focus = centre, Hops = 1 });

        Assert.Contains(model.Nodes, n => n.Id == "inbound.md");
    }

    [Fact]
    public void LevelOfDetailCollapsesFoldersOnceTheGraphIsTooBigToDraw()
    {
        var files = new List<(string, string)>();

        for (int i = 0; i < 20; i++)
        {
            files.Add(($"alpha/{i}.md", $"[[beta/{i}]]\n"));
            files.Add(($"beta/{i}.md", $"# Beta {i}\n"));
        }

        GraphModel full = Build([.. files]);
        GraphModel folded = full.WithLevelOfDetail(maxNodes: 10);

        Assert.Equal(40, full.Nodes.Count);
        Assert.Equal(2, folded.Nodes.Count);
        Assert.All(folded.Nodes, n => Assert.Equal(GraphNodeKind.Cluster, n.Kind));

        // Twenty alpha pages became one node that says so, and the twenty edges between
        // the folders became one edge of weight twenty.
        Assert.Equal(20, Assert.Single(folded.Nodes, n => n.Id == "alpha").Weight);
        Assert.Equal(20, Assert.Single(folded.Edges).Weight);
    }

    [Fact]
    public void LevelOfDetailLeavesASmallGraphAlone()
    {
        GraphModel model = Build(("a.md", "[[b]]\n"), ("b.md", "# B\n"));

        Assert.Same(model, model.WithLevelOfDetail(maxNodes: 1500));
    }

    [Fact]
    public void ATortureVaultBuildsWithoutThrowing()
    {
        GraphModel model = GraphModel.Build(LinkGraph.Build(FixtureVaults.Load("torture")));

        Assert.NotEmpty(model.Nodes);
        Assert.All(model.Edges, e =>
        {
            Assert.InRange(e.Source, 0, model.Nodes.Count - 1);
            Assert.InRange(e.Target, 0, model.Nodes.Count - 1);
        });
    }

    private static GraphModel Build(params (string Path, string Content)[] files) =>
        GraphModel.Build(LinkGraph.Build(Vault(files)));

    private static GraphModel Build(GraphOptions options, params (string Path, string Content)[] files) =>
        GraphModel.Build(LinkGraph.Build(Vault(files)), options);

    private static VaultSnapshot Vault(params (string Path, string Content)[] files) =>
        new()
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build([.. files.Select(f => TestVault.CreateDocument(f.Path, f.Content))]),
        };
}
