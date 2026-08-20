using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Detangle.Core.Vault;
using Detangle.Rendering.Controls;
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
            Control control = new DocumentRenderer(DocumentTheme.Dark).Render(rendered);
            Layout(control);

            SelectableTextBlock codeLine = control.GetLogicalDescendants()
                .OfType<SelectableTextBlock>()
                .First(block => block.Inlines?.OfType<Run>().Any() == true);

            List<Run> runs = [.. codeLine.Inlines!.OfType<Run>()];

            Assert.True(runs.Count > 1);
            Assert.Contains(runs, run => run.Foreground is not null);
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
    public void CollapsibleCalloutsRenderAsExpanders()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "> [!tip]- Closed\n> Body.\n")).Render("page.md");

        app.Invoke(() =>
        {
            Control control = new DocumentRenderer(DocumentTheme.Light).Render(rendered);
            Layout(control);

            Expander expander = Assert.Single(control.GetLogicalDescendants().OfType<Expander>());

            Assert.False(expander.IsExpanded);
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
