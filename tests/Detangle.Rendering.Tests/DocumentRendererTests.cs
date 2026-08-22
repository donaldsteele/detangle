using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Detangle.Core.Vault;
using Detangle.Highlighting;
using Detangle.Rendering.Controls;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Highlighting;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the render model to Avalonia translation. They run against a real headless
/// platform and lay the controls out, because the failures worth catching here — a
/// malformed brush, an inline that cannot host a control, a measure that throws — only
/// happen when Avalonia actually runs.
/// </summary>
[Collection(HeadlessCollection.Name)]
public class DocumentRendererTests(HeadlessAppFixture app)
{
    [Fact]
    public void RendersTheTortureVaultWithoutThrowing()
    {
        VaultSnapshot vault = VaultScanner.Scan(FixturePath("torture"));
        var builder = new RenderModelBuilder(vault);

        app.Invoke(() =>
        {
            var renderer = new DocumentRenderer(DocumentTheme.Light);

            foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
            {
                Control control = renderer.Render(builder.Build(document));

                Layout(control);

                Assert.NotEmpty(control.GetLogicalDescendants());
            }
        });
    }

    [Theory]
    [InlineData("llm-wiki")]
    [InlineData("obsidian")]
    [InlineData("logseq")]
    [InlineData("dendron")]
    public void RendersEveryFixtureVault(string vaultName)
    {
        VaultSnapshot vault = VaultScanner.Scan(FixturePath(vaultName));
        var builder = new RenderModelBuilder(vault);

        app.Invoke(() =>
        {
            var renderer = new DocumentRenderer(DocumentTheme.Dark);

            foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
            {
                Layout(renderer.Render(builder.Build(document)));
            }
        });
    }

    [Fact]
    public void ResolvedLinksAreClickableAndReportTheirResolution()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "[[My Target]]\n"),
            ("my-target.md", "# My Target")).Render("page.md");

        app.Invoke(() =>
        {
            var renderer = new DocumentRenderer(DocumentTheme.Light);
            LinkActivatedEventArgs? activated = null;
            renderer.LinkActivated += (_, e) => activated = e;

            Control control = renderer.Render(rendered);
            Layout(control);

            Button link = control.GetLogicalDescendants().OfType<Button>().First();
            link.Command?.Execute(null);
            link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.NotNull(activated);
            Assert.Equal("my-target.md", activated!.Resolution.Target?.RelativePath);
        });
    }

    [Fact]
    public void UnresolvedLinksAreStyledDifferentlyFromResolvedOnes()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "[[my-target]] and [[nowhere]]\n"),
            ("my-target.md", "# My Target")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            List<TextBlock> labels =
            [
                .. control.GetLogicalDescendants().OfType<Button>()
                    .Select(button => button.Content).OfType<TextBlock>(),
            ];

            Assert.Equal(2, labels.Count);
            Assert.NotEqual(labels[0].Foreground, labels[1].Foreground);
            Assert.Equal(DocumentTheme.Light.UnresolvedLink, labels[1].Foreground);
        });
    }

    [Fact]
    public void ExactLinksCarryNoDecorationAndNormalizedOnesDo()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "[[my-target]] and [[My Target Two]]\n"),
            ("my-target.md", "# One"),
            ("my-target-two.md", "# Two")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            List<TextBlock> labels =
            [
                .. control.GetLogicalDescendants().OfType<Button>()
                    .Select(button => button.Content).OfType<TextBlock>(),
            ];

            Assert.Null(labels[0].TextDecorations);
            Assert.NotNull(labels[1].TextDecorations);
        });
    }

    [Fact]
    public void FencedCodeIsHighlightedIntoSeveralRuns()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "```csharp\nvar x = 1;\n```\n")).Render("page.md");

        app.Invoke(() =>
        {
            // The highlighter is named rather than inherited: no head runs in a test
            // process, so the renderer's own default here is the plain fallback.
            Control control = new DocumentRenderer(
                DocumentTheme.Dark,
                highlighter: new TextMateCodeHighlighter(HighlightTheme.Dark)).Render(rendered);

            Layout(control);

            SelectableTextBlock codeLine = control.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .First(block => block.Inlines?.OfType<Run>().Any() == true);

            List<Run> runs = [.. codeLine.Inlines!.OfType<Run>()];

            Assert.True(runs.Count > 1);
            Assert.Contains(runs, run => run.Foreground is not null);
        });
    }

    /// <summary>
    /// What the WebAssembly demo does with every fence, since it ships no grammars: one
    /// readable monospaced line in the body colour. Not an exception, and not an empty
    /// block.
    /// </summary>
    [Fact]
    public void FencedCodeStaysReadableWithNoHighlighterInstalled()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "```csharp\nvar x = 1;\n```\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(
                DocumentTheme.Dark,
                highlighter: PlainCodeHighlighter.Instance).Render(rendered);

            Layout(control);

            SelectableTextBlock codeLine = control.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .First(block => block.Inlines?.OfType<Run>().Any() == true);

            Run run = Assert.Single(codeLine.Inlines!.OfType<Run>());

            Assert.Equal("var x = 1;", run.Text);
            Assert.Equal(DocumentTheme.Dark.Foreground, run.Foreground);
        });
    }

    [Fact]
    public void CalloutsUseTheirKindsAccent()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "> [!warning] Careful\n> Body.\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            Border card = control.GetLogicalDescendants().OfType<Border>()
                .First(border => border.BorderThickness.Left == 3);

            Assert.Equal(
                DocumentTheme.Light.AccentFor("warning").ToString(),
                card.BorderBrush?.ToString());
        });
    }

    [Fact]
    public void CollapsibleCalloutsStartClosed()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "> [!tip]- Closed\n> Body.\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            // The disclosure is a toggle plus a panel rather than an Expander: Expander
            // brings a transition, and several of them on a page can leave the measure
            // pass never settling.
            ToggleButton toggle = Assert.Single(
                control.GetLogicalDescendants().OfType<ToggleButton>(), t => t is not CheckBox);

            Assert.False(toggle.IsChecked);
            Assert.Contains(
                control.GetLogicalDescendants().OfType<StackPanel>(),
                panel => !panel.IsVisible);
        });
    }

    [Fact]
    public void MissingImagesRenderAsTheirAlternateText()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "![A missing picture](nowhere.png)\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            Assert.Contains(
                control.GetLogicalDescendants().OfType<TextBlock>(),
                block => block.Text?.Contains("missing image", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public void TaskListsRenderAsReadOnlyCheckboxes()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "- [x] done\n- [ ] todo\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            List<CheckBox> boxes = [.. control.GetLogicalDescendants().OfType<CheckBox>()];

            Assert.Equal([true, false], boxes.Select(box => box.IsChecked));
            Assert.All(boxes, box => Assert.False(box.IsHitTestVisible));
        });
    }

    [Fact]
    public void DrawsRenderedDiagramsAndErrorCardsAlike()
    {
        VaultSnapshot vault = VaultScanner.Scan(FixturePath("torture"));

        var builder = new RenderModelBuilder(
            vault,
            options: new RenderOptions { DiagramRenderer = new MermaiderDiagramRenderer() });

        RenderDocument rendered = builder.Build(
            vault.Index.ByRelativePath("reader.md").Single());

        List<DiagramRenderBlock> diagrams = [.. rendered.Blocks.OfType<DiagramRenderBlock>()];

        // A good Mermaid fence, a DBML schema, and a broken fence — the reader has to
        // survive all three on one page.
        Assert.Contains(diagrams, d => d.Kind == DiagramKind.Mermaid && d.IsRendered);
        Assert.Contains(diagrams, d => d.Kind == DiagramKind.Dbml && d.IsRendered);
        Assert.Contains(diagrams, d => !d.IsRendered && d.Diagnostics.Count > 0);

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            Assert.NotEmpty(control.GetLogicalDescendants().OfType<Image>());
        });
    }

    [Theory]
    [InlineData(560)]
    [InlineData(760)]
    public void ATableOfProseWrapsInsteadOfRunningOffThePage(int pane)
    {
        // Cells were laid out inside a horizontally scrolling viewport, which measures its
        // content at infinite width - and a star-sized column given infinite width behaves
        // exactly like an Auto one. Nothing wrapped: this table came out 728 points wide
        // whatever the pane, so in a 559 point reading pane the last 169 points of every
        // row were simply cut off.
        //
        // Laid out in a window on purpose. A control measured on its own outside one
        // reports a size of zero here, which an assertion about width passes without ever
        // touching the thing it is supposed to be testing.
        const string Markdown = """
            | Written | Resolves by |
            |---|---|
            | [[Attention Is All You Need]] | normalized name — the file is `attention-is-all-you-need.md` |
            | [[entities/attention-is-all-you-need]] | exact vault path |
            | [attention](../entities/attention-is-all-you-need.md) | an ordinary markdown link, relative |
            | [[wiki/setup]] | folder index — `wiki/setup/index.md`, a folder rather than a file |
            """;

        (double width, double height) = LayOutTable(Markdown, pane);

        Assert.True(width <= pane, $"the table came out {width:0} points wide in a {pane} point pane.");

        // And it wrapped to get there rather than being clipped: four rows of prose at this
        // width cannot fit on one line each.
        Assert.True(height > 120, $"the table is only {height:0} points tall, so nothing wrapped.");
    }

    /// <summary>
    /// Renders a scrap of markdown in a window of the given width and reports the size the
    /// table was actually given.
    /// </summary>
    private (double Width, double Height) LayOutTable(string markdown, double pane)
    {
        string root = Path.Combine(Path.GetTempPath(), "detangle-table-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.md"), markdown);

        try
        {
            VaultSnapshot vault = VaultScanner.Scan(root);
            var builder = new RenderModelBuilder(vault);
            Rect bounds = default;

            app.Invoke(() =>
            {
                Control rendered = new DocumentRenderer(DocumentTheme.Light)
                    .Render(builder.Build(vault.Documents.Single(d => d.IsMarkdown)));

                var window = new Window { Width = pane, Height = 900, Content = rendered };

                window.Show();
                Dispatcher.UIThread.RunJobs();

                bounds = rendered.GetLogicalDescendants().OfType<Grid>()
                    .First(g => g.ColumnDefinitions.Count == 2)
                    .Bounds;

                window.Close();
            });

            return (bounds.Width, bounds.Height);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Layout(Control control)
    {
        control.Measure(new Size(900, double.PositiveInfinity));
        control.Arrange(new Rect(control.DesiredSize));
    }

    private static string FixturePath(string vaultName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "vaults", vaultName);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Fixture vault {vaultName} was not found.");
    }
}
