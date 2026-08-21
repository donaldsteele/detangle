using Avalonia;
using Avalonia.Controls;
using Detangle.Core.Graph;
using Detangle.Core.Vault;
using Detangle.Rendering.Controls;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the graph canvas (plan.md section 6.4). The picture itself cannot be
/// asserted, so these cover what the reader can actually do to it: hit a node, open it,
/// pan, zoom, drag, and fold a big vault down to something drawable.
/// </summary>
[Collection(HeadlessCollection.Name)]
public class GraphCanvasTests(HeadlessAppFixture app)
{
    [Fact]
    public void AnEmptyCanvasMountsWithoutThrowing() =>
        app.Invoke(() => Assert.Empty(Mount().Model.Nodes));

    [Fact]
    public void ShowingAGraphLaysItOutAndFitsItToTheView() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();

            canvas.Show(Ring(12));
            canvas.FitToView();

            // Every node has to land inside the view, or "fit" means nothing.
            Assert.All(canvas.Model.Nodes, n =>
            {
                Point point = canvas.PositionOf(n.Index);

                Assert.InRange(point.X, 0, canvas.Bounds.Width);
                Assert.InRange(point.Y, 0, canvas.Bounds.Height);
            });
        });

    [Fact]
    public void ANodeIsWhereTheCanvasSaysItIs() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            GraphNode node = canvas.Model.Nodes[3];

            Assert.Equal(node.Index, canvas.HitTest(canvas.PositionOf(node.Index)));
        });

    [Fact]
    public void ClickingANodeOpensIt() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            GraphNode? opened = null;
            canvas.NodeActivated += (_, args) => opened = args.Node;

            GraphNode target = canvas.Model.Nodes[3];

            canvas.Press(canvas.PositionOf(target.Index));
            canvas.Release();

            Assert.Equal(target.Id, opened?.Id);
        });

    [Fact]
    public void ClickingEmptySpaceOpensNothing() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(4));
            canvas.FitToView();

            bool opened = false;
            canvas.NodeActivated += (_, _) => opened = true;

            canvas.Press(new Point(2, 2));
            canvas.Release();

            Assert.False(opened);
        });

    [Fact]
    public void DraggingMovesThePageRatherThanOpeningIt() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            bool opened = false;
            canvas.NodeActivated += (_, _) => opened = true;

            GraphNode target = canvas.Model.Nodes[2];
            Point start = canvas.PositionOf(target.Index);
            var end = new Point(start.X + 60, start.Y + 40);

            canvas.Press(start);
            canvas.MoveTo(end);
            canvas.Release();

            Assert.False(opened, "a drag is not a click");

            Point moved = canvas.PositionOf(target.Index);

            Assert.Equal(end.X, moved.X, 1);
            Assert.Equal(end.Y, moved.Y, 1);
        });

    [Fact]
    public void DraggingEmptySpacePansThePicture() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            Point before = canvas.PositionOf(0);

            canvas.Press(new Point(2, 2));
            canvas.MoveTo(new Point(42, 32));
            canvas.Release();

            Point after = canvas.PositionOf(0);

            Assert.Equal(before.X + 40, after.X, 1);
            Assert.Equal(before.Y + 30, after.Y, 1);
        });

    [Fact]
    public void HoveringReportsTheNodeUnderThePointer() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            var seen = new List<GraphNode?>();
            canvas.NodeHovered += (_, node) => seen.Add(node);

            GraphNode target = canvas.Model.Nodes[5];

            canvas.MoveTo(canvas.PositionOf(target.Index));

            Assert.Equal(target.Id, canvas.Hovered?.Id);
            Assert.Contains(seen, n => n?.Id == target.Id);
        });

    [Fact]
    public void EmptySpaceIsNotANode() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(4));
            canvas.FitToView();

            Assert.Equal(-1, canvas.HitTest(new Point(2, 2)));
        });

    [Fact]
    public void ZoomingKeepsThePointUnderTheCursorStill() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            Point before = canvas.PositionOf(1);

            canvas.Zoom(before, 1);

            Point after = canvas.PositionOf(1);

            Assert.Equal(before.X, after.X, 1);
            Assert.Equal(before.Y, after.Y, 1);
        });

    [Fact]
    public void CentringPutsANodeInTheMiddle() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(8));
            canvas.FitToView();

            GraphNode target = canvas.Model.Nodes[6];

            canvas.CentreOn(target);

            Point centre = canvas.PositionOf(target.Index);

            Assert.Equal(canvas.Bounds.Width / 2, centre.X, 1);
            Assert.Equal(canvas.Bounds.Height / 2, centre.Y, 1);
        });

    [Fact]
    public void TheSimulationStopsOnceItHasSettled() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            canvas.Show(Ring(6));

            for (int i = 0; i < 2000 && canvas.IsSimulating; i++)
            {
                canvas.Advance();
            }

            Assert.False(canvas.IsSimulating);
        });

    [Fact]
    public void OrphansMissingTargetsAndTypedPagesAllDraw() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount(DocumentTheme.Dark);

            GraphModel model = GraphModel.Build(Core.Graph.LinkGraph.Build(Vault(
                ("a.md", "[[nowhere]] [[folder/b]]\n"),
                ("folder/b.md", "---\ntype: concept\n---\n\n# B\n"),
                ("lonely.md", "# Lonely\n"))));

            canvas.Show(model);
            canvas.Advance();

            // Each of the three is drawn differently; this asserts only that having all
            // of them on screen at once does not throw.
            Assert.Contains(model.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);
            Assert.Contains(model.Nodes, n => n.IsOrphan);
            Assert.Contains(model.Nodes, n => n.Type == "concept");
        });

    [Fact]
    public void ALargeGraphFoldsToClustersAndStillDraws() =>
        app.Invoke(() =>
        {
            GraphCanvas canvas = Mount();
            GraphModel folded = Ring(200).WithLevelOfDetail(maxNodes: 20);

            canvas.Show(folded);
            canvas.Advance();

            Assert.All(folded.Nodes, n => Assert.Equal(GraphNodeKind.Cluster, n.Kind));
        });

    private static GraphCanvas Mount(DocumentTheme? theme = null)
    {
        var canvas = new GraphCanvas(theme);
        var window = new Window { Width = 600, Height = 400, Content = canvas };

        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));

        return canvas;
    }

    private static GraphModel Ring(int count)
    {
        var files = new List<(string, string)>();

        for (int i = 0; i < count; i++)
        {
            files.Add(($"folder-{i % 4}/page-{i}.md", $"[[page-{(i + 1) % count}]]\n"));
        }

        return GraphModel.Build(Core.Graph.LinkGraph.Build(Vault([.. files])));
    }

    private static VaultSnapshot Vault(params (string Path, string Content)[] files) =>
        RenderTestVault.Build(files).Vault;
}
