using Detangle.Core.Linking;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>Tests for the Markdig AST to render-model translation.</summary>
public class RenderModelBuilderTests
{
    [Fact]
    public void RendersHeadingsWithTheirAnchors()
    {
        IReadOnlyList<RenderBlock> blocks = RenderTestVault.BodyOf(
            "# Title\n\n## Duplicate\n\n## Duplicate\n");

        Assert.Equal(
            ["title", "duplicate", "duplicate-1"],
            blocks.OfType<HeadingRenderBlock>().Select(h => h.Slug));
    }

    [Fact]
    public void RendersNestedEmphasis()
    {
        var paragraph = Assert.IsType<ParagraphRenderBlock>(
            RenderTestVault.BodyOf("**bold with *italic* inside**\n")[0]);

        var bold = Assert.IsType<StyleRun>(paragraph.Inlines[0]);
        Assert.Equal(TextStyle.Bold, bold.Style);
        Assert.Contains(bold.Children, i => i is StyleRun { Style: TextStyle.Italic });
    }

    [Theory]
    [InlineData("~~struck~~", TextStyle.Strikethrough)]
    [InlineData("==highlight==", TextStyle.Highlight)]
    [InlineData("^super^", TextStyle.Superscript)]
    [InlineData("~sub~", TextStyle.Subscript)]
    public void RendersEmphasisExtras(string markdown, TextStyle expected)
    {
        var paragraph = Assert.IsType<ParagraphRenderBlock>(RenderTestVault.BodyOf(markdown)[0]);

        Assert.Equal(expected, Assert.IsType<StyleRun>(paragraph.Inlines[0]).Style);
    }

    [Fact]
    public void DiagramFencesBecomeDiagramBlocksAndKeepTheirSource()
    {
        var diagram = Assert.IsType<DiagramRenderBlock>(
            RenderTestVault.BodyOf("```mermaid\ngraph TD;\nA-->B;\n```\n")[0]);

        Assert.Equal(DiagramKind.Mermaid, diagram.Kind);
        Assert.Contains("A-->B", diagram.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersFencedCodeWithItsLanguage()
    {
        var code = Assert.IsType<CodeRenderBlock>(
            RenderTestVault.BodyOf("```csharp\nvar x = 1;\n```\n")[0]);

        Assert.Equal("csharp", code.Language);
        Assert.False(code.IsDiagram);
    }

    [Fact]
    public void RendersBlockAndInlineMath()
    {
        IReadOnlyList<RenderBlock> blocks = RenderTestVault.BodyOf(
            "Inline $E = mc^2$ here.\n\n$$\n\\int_0^1 x\\,dx\n$$\n");

        var paragraph = Assert.IsType<ParagraphRenderBlock>(blocks[0]);
        Assert.Contains(paragraph.Inlines, i => i is MathRun);

        var math = Assert.IsType<MathRenderBlock>(blocks[1]);
        Assert.Contains("int_0^1", math.Source);
    }

    [Fact]
    public void RendersTablesWithAlignmentsAndHeaderRows()
    {
        var table = Assert.IsType<TableRenderBlock>(RenderTestVault.BodyOf(
            "| Left | Center | Right |\n|:-----|:------:|------:|\n| a | b | c |\n")[0]);

        Assert.Equal(
            [ColumnAlignment.Left, ColumnAlignment.Center, ColumnAlignment.Right],
            table.Alignments);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
        Assert.Equal(3, table.Rows[1].Cells.Count);
    }

    [Fact]
    public void RendersTaskLists()
    {
        var list = Assert.IsType<ListRenderBlock>(
            RenderTestVault.BodyOf("- [ ] todo\n- [x] done\n- plain\n")[0]);

        Assert.Equal(
            [TaskState.Unchecked, TaskState.Checked, TaskState.None],
            list.Items.Select(i => i.Task));
    }

    [Fact]
    public void RendersOrderedListsFromTheirStartNumber()
    {
        var list = Assert.IsType<ListRenderBlock>(RenderTestVault.BodyOf("3. three\n4. four\n")[0]);

        Assert.True(list.IsOrdered);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public void RendersFootnotes()
    {
        IReadOnlyList<RenderBlock> blocks = RenderTestVault.BodyOf(
            "Claim[^a].\n\n[^a]: The evidence.\n");

        var paragraph = Assert.IsType<ParagraphRenderBlock>(blocks[0]);
        Assert.Contains(paragraph.Inlines, i => i is FootnoteReferenceRun);

        var footnotes = Assert.IsType<FootnotesRenderBlock>(blocks[^1]);
        Assert.Equal("a", footnotes.Notes[0].Label);
    }

    [Fact]
    public void DropsRawHtmlRatherThanRenderingIt()
    {
        // HTML is parsed so that it hides the same things from the reader as from the
        // graph, then discarded — nothing in an Avalonia control tree can honour markup.
        Assert.Empty(RenderTestVault.BodyOf("<script>alert(1)</script>\n"));
    }

    [Fact]
    public void ALinkInsideAnHtmlCommentIsNotALink()
    {
        // The reader and the graph must agree about what counts as a link; a comment
        // hides its contents from both.
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "<!-- [[target]] -->\n\n[[target]]\n"),
            ("target.md", "# Target")).Render("page.md");

        Assert.Single(rendered.Resolutions);
    }

    [Fact]
    public void BuildsThePropertiesCardFromFrontmatter()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "---\ntitle: A Page\ntype: concept\nrelated:\n  - other\n---\n\n# Body\n"),
            ("other.md", "# Other")).Render("page.md");

        var properties = Assert.IsType<PropertiesRenderBlock>(rendered.Blocks[0]);

        Assert.Equal("A Page", properties.Frontmatter.Title);
        Assert.Equal("other.md", Assert.Single(properties.References).Target?.RelativePath);
    }

    [Fact]
    public void OmitsThePropertiesCardWhenThereIsNoFrontmatter() =>
        Assert.DoesNotContain(
            RenderTestVault.Build(("page.md", "# Body\n")).Render("page.md").Blocks,
            b => b is PropertiesRenderBlock);

    [Fact]
    public void CarriesTheResolutionOnEveryLink()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "[[My Target]] and [[nowhere]]\n"),
            ("my-target.md", "# My Target")).Render("page.md");

        Assert.Equal(2, rendered.Resolutions.Count);
        Assert.Single(rendered.BrokenLinks);

        var paragraph = Assert.IsType<ParagraphRenderBlock>(rendered.Blocks[0]);
        var link = Assert.IsType<LinkRun>(paragraph.Inlines[0]);

        Assert.Equal(ResolutionRule.NormalizedName, link.Resolution.Rule);
        Assert.Equal(ResolutionConfidence.Normalized, link.Resolution.Confidence);
    }

    [Fact]
    public void UsesTheTargetsDisplayNameWhenALinkHasNoLabel()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "[[lang.python.basics]]\n"),
            ("lang.python.basics.md", "---\ntitle: Basics\n---\n\n# Basics")).Render("page.md");

        var link = Assert.IsType<LinkRun>(
            Assert.IsType<ParagraphRenderBlock>(rendered.Blocks[0]).Inlines[0]);

        Assert.Equal("Basics", Assert.IsType<TextRun>(link.Children[0]).Text);
    }

    [Fact]
    public void KeepsWhatWasWrittenWhenALinkDoesNotResolve()
    {
        RenderDocument rendered = RenderTestVault.Build(("page.md", "[[nowhere at all]]\n")).Render("page.md");

        var link = Assert.IsType<LinkRun>(
            Assert.IsType<ParagraphRenderBlock>(rendered.Blocks[0]).Inlines[0]);

        Assert.Equal("nowhere at all", Assert.IsType<TextRun>(link.Children[0]).Text);
    }

    [Fact]
    public void ExternalLinksKeepTheirUrlAndAreNeverResolved()
    {
        var link = Assert.IsType<LinkRun>(
            Assert.IsType<ParagraphRenderBlock>(
                RenderTestVault.BodyOf("[Site](https://example.com/page)\n")[0]).Inlines[0]);

        Assert.Equal("https://example.com/page", link.Url);
        Assert.Equal(ResolutionRule.NotAttempted, link.Resolution.Rule);
    }

    [Fact]
    public void ImageEmbedsCarryTheirSize()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "![[diagram.png|300x200]]\n"),
            ("assets/diagram.png", string.Empty)).Render("page.md");

        var image = Assert.IsType<ImageRun>(
            Assert.IsType<ParagraphRenderBlock>(rendered.Blocks[0]).Inlines[0]);

        Assert.Equal(300, image.Width);
        Assert.Equal(200, image.Height);
        Assert.Equal("assets/diagram.png", image.Resolution.Target?.RelativePath);
    }

    [Fact]
    public void MarkdownImagesResolveThroughTheChainToo()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("notes/page.md", "![A diagram](../assets/diagram.png)\n"),
            ("assets/diagram.png", string.Empty)).Render("notes/page.md");

        var image = Assert.IsType<ImageRun>(
            Assert.IsType<ParagraphRenderBlock>(rendered.Blocks[0]).Inlines[0]);

        Assert.Equal("assets/diagram.png", image.Resolution.Target?.RelativePath);
        Assert.Equal("A diagram", image.AlternateText);
    }
}
