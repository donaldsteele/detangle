using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the graph layout. A force simulation has no single right answer, so these
/// assert the properties a reader actually depends on: it is the same every time, it
/// stops, and linked pages end up near each other.
/// </summary>
public class ForceLayoutTests
{
    [Fact]
    public void TheSameVaultLaysOutTheSameWayTwice()
    {
        GraphModel model = Build(20);

        ForceLayout first = Run(model, 60);
        ForceLayout second = Run(model, 60);

        for (int i = 0; i < model.Nodes.Count; i++)
        {
            Assert.Equal(first.X[i], second.X[i], 9);
            Assert.Equal(first.Y[i], second.Y[i], 9);
        }
    }

    [Fact]
    public void ItCools()
    {
        var layout = new ForceLayout(Build(20));

        Assert.False(layout.IsSettled);

        layout.Step(400);

        Assert.True(layout.IsSettled);
    }

    [Fact]
    public void SteppingASettledLayoutDoesNothing()
    {
        ForceLayout layout = Run(Build(10), 400);
        double x = layout.X[0];

        layout.Step(50);

        Assert.Equal(x, layout.X[0], 12);
    }

    [Fact]
    public void ReheatingLetsItMoveAgain()
    {
        ForceLayout layout = Run(Build(10), 400);

        layout.Reheat();

        Assert.False(layout.IsSettled);
    }

    [Fact]
    public void LinkedPagesEndUpCloserThanUnlinkedOnes()
    {
        VaultSnapshot vault = Vault(
            ("a.md", "[[b]]\n"),
            ("b.md", "[[a]]\n"),
            ("far.md", "# Far\n"));

        GraphModel model = GraphModel.Build(LinkGraph.Build(vault));
        ForceLayout layout = Run(model, 300);

        int a = Index(model, "a.md");
        int b = Index(model, "b.md");
        int far = Index(model, "far.md");

        Assert.True(Distance(layout, a, b) < Distance(layout, a, far));
    }

    [Fact]
    public void NodesDoNotLandOnTopOfEachOther()
    {
        GraphModel model = Build(40);
        ForceLayout layout = Run(model, 300);

        for (int i = 0; i < model.Nodes.Count; i++)
        {
            for (int j = i + 1; j < model.Nodes.Count; j++)
            {
                Assert.True(
                    Distance(layout, i, j) > 0.5,
                    $"nodes {i} and {j} are on top of each other");
            }
        }
    }

    [Fact]
    public void EveryPositionStaysARealNumber()
    {
        GraphModel model = Build(50);
        ForceLayout layout = Run(model, 300);

        for (int i = 0; i < model.Nodes.Count; i++)
        {
            Assert.True(double.IsFinite(layout.X[i]) && double.IsFinite(layout.Y[i]));
        }
    }

    [Fact]
    public void AnEmptyGraphLaysOutWithoutThrowing()
    {
        var layout = new ForceLayout(GraphModel.Empty);

        layout.Step(10);

        Assert.Equal((0, 0, 0, 0), layout.Bounds());
    }

    [Fact]
    public void PlacingANodePinsIt()
    {
        var layout = new ForceLayout(Build(10));

        layout.Place(3, 100, -50);

        Assert.Equal(100, layout.X[3]);
        Assert.Equal(-50, layout.Y[3]);
    }

    [Fact]
    public void BoundsCoverEveryNode()
    {
        GraphModel model = Build(30);
        ForceLayout layout = Run(model, 100);

        (double minX, double minY, double maxX, double maxY) = layout.Bounds();

        for (int i = 0; i < model.Nodes.Count; i++)
        {
            Assert.InRange(layout.X[i], minX, maxX);
            Assert.InRange(layout.Y[i], minY, maxY);
        }
    }

    private static double Distance(ForceLayout layout, int a, int b)
    {
        double dx = layout.X[a] - layout.X[b];
        double dy = layout.Y[a] - layout.Y[b];

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int Index(GraphModel model, string id) =>
        model.Nodes.Single(n => n.Id == id).Index;

    private static ForceLayout Run(GraphModel model, int steps)
    {
        var layout = new ForceLayout(model);
        layout.Step(steps);

        return layout;
    }

    /// <summary>A ring of pages, each linking to the next — connected but not a hairball.</summary>
    private static GraphModel Build(int count)
    {
        var files = new List<(string, string)>();

        for (int i = 0; i < count; i++)
        {
            files.Add(($"page-{i}.md", $"[[page-{(i + 1) % count}]]\n"));
        }

        return GraphModel.Build(LinkGraph.Build(Vault([.. files])));
    }

    private static VaultSnapshot Vault(params (string Path, string Content)[] files) =>
        new()
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build([.. files.Select(f => TestVault.CreateDocument(f.Path, f.Content))]),
        };
}
