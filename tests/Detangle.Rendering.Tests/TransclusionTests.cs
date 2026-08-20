using Detangle.Core.Vault;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for "![[note]]" embeds (plan.md section 6.1): inlined content, anchored
/// sections, a source chip's worth of provenance, and cycle detection.
/// </summary>
public class TransclusionTests
{
    [Fact]
    public void EmbedsAWholeDocument()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(RenderTestVault.Build(
            ("page.md", "![[source]]\n"),
            ("source.md", "# Source\n\nBorrowed text.\n")).Body("page.md")[0]);

        Assert.Equal("source.md", transclusion.Resolution.Target?.RelativePath);
        Assert.Null(transclusion.Error);
        Assert.Equal(2, transclusion.Blocks.Count);
    }

    [Fact]
    public void EmbedsOnlyTheAnchoredSection()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(RenderTestVault.Build(
            ("page.md", "![[source#Second]]\n"),
            ("source.md", "# Source\n\n## First\n\nOne.\n\n## Second\n\nTwo.\n\n## Third\n\nThree.\n"))
            .Body("page.md")[0]);

        Assert.Equal("Second", Assert.IsType<HeadingRenderBlock>(transclusion.Blocks[0]).Text);
        Assert.Equal(2, transclusion.Blocks.Count);
    }

    [Fact]
    public void AnAnchoredSectionRunsToTheNextHeadingOfTheSameOrHigherLevel()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(RenderTestVault.Build(
            ("page.md", "![[source#Outer]]\n"),
            ("source.md", "## Outer\n\nOne.\n\n### Inner\n\nTwo.\n\n## Next\n\nThree.\n"))
            .Body("page.md")[0]);

        // Outer, its paragraph, Inner, and Inner's paragraph — but not Next.
        Assert.Equal(4, transclusion.Blocks.Count);
        Assert.DoesNotContain(
            transclusion.Blocks.OfType<HeadingRenderBlock>(), h => h.Text == "Next");
    }

    [Fact]
    public void EmbedsASingleBlockByItsMarker()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(RenderTestVault.Build(
            ("page.md", "![[source#^pick-me]]\n"),
            ("source.md", "# Source\n\nFirst paragraph.\n\nSecond paragraph. ^pick-me\n\nThird.\n"))
            .Body("page.md")[0]);

        RenderBlock only = Assert.Single(transclusion.Blocks);

        Assert.Contains(
            "Second paragraph",
            RenderModelBuilder.ToPlainText(Assert.IsType<ParagraphRenderBlock>(only).Inlines),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnchorsThatMatchNothingEmbedNothing()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(RenderTestVault.Build(
            ("page.md", "![[source#Missing]]\n"),
            ("source.md", "# Source\n\nText.\n")).Body("page.md")[0]);

        Assert.Empty(transclusion.Blocks);
        Assert.Equal("source.md", transclusion.Resolution.Target?.RelativePath);
    }

    [Fact]
    public void AnUnresolvedEmbedExplainsItself()
    {
        var transclusion = Assert.IsType<TransclusionRenderBlock>(
            RenderTestVault.Build(("page.md", "![[nowhere]]\n")).Body("page.md")[0]);

        Assert.Empty(transclusion.Blocks);
        Assert.Contains("nowhere", transclusion.Error);
    }

    [Fact]
    public void DetectsDirectCycles()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("a.md", "![[a]]\n")).Render("a.md");

        var transclusion = Assert.IsType<TransclusionRenderBlock>(rendered.Blocks[0]);

        Assert.Contains("cycle", transclusion.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(rendered.Diagnostics);
    }

    [Fact]
    public void DetectsIndirectCycles()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("a.md", "![[b]]\n"),
            ("b.md", "![[a]]\n")).Render("a.md");

        var outer = Assert.IsType<TransclusionRenderBlock>(rendered.Blocks[0]);
        var inner = Assert.IsType<TransclusionRenderBlock>(outer.Blocks[0]);

        Assert.Contains("cycle", inner.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StopsAtTheDepthLimit()
    {
        RenderTestVault vault = RenderTestVault.Build(
            VaultFlavor.Generic,
            new RenderOptions { MaxTransclusionDepth = 2 },
            ("a.md", "![[b]]\n"),
            ("b.md", "![[c]]\n"),
            ("c.md", "![[d]]\n"),
            ("d.md", "Bottom.\n"));

        var b = Assert.IsType<TransclusionRenderBlock>(vault.Body("a.md")[0]);
        var c = Assert.IsType<TransclusionRenderBlock>(b.Blocks[0]);

        Assert.Contains("2 deep", Assert.IsType<TransclusionRenderBlock>(c.Blocks[0]).Error);
    }

    [Fact]
    public void AnEmbedInsideASentenceStaysInline()
    {
        // Promoting it to a block would reorder the author's words, so a mid-sentence
        // embed renders as a link instead.
        var paragraph = Assert.IsType<ParagraphRenderBlock>(RenderTestVault.Build(
            ("page.md", "See ![[source]] for details.\n"),
            ("source.md", "# Source\n")).Body("page.md")[0]);

        Assert.Contains(paragraph.Inlines, i => i is LinkRun { Resolution.Link.IsEmbed: true });
    }

    [Fact]
    public void SeveralEmbedsOnConsecutiveLinesEachBecomeABlock()
    {
        IReadOnlyList<RenderBlock> blocks = RenderTestVault.Build(
            ("page.md", "![[one]]\n![[two]]\n"),
            ("one.md", "# One\n"),
            ("two.md", "# Two\n")).Body("page.md");

        var group = Assert.IsType<QuoteRenderBlock>(blocks[0]);

        Assert.Equal(2, group.Blocks.Count);
        Assert.All(group.Blocks, b => Assert.IsType<TransclusionRenderBlock>(b));
    }

    [Fact]
    public void EmbeddedDocumentsResolveTheirOwnLinksRelativeToThemselves()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "![[deep/source]]\n"),
            ("deep/source.md", "[[sibling]]\n"),
            ("deep/sibling.md", "# Sibling")).Render("page.md");

        Assert.Contains(rendered.Resolutions, r => r.Target?.RelativePath == "deep/sibling.md");
    }

    [Fact]
    public void EmbeddedHeadingsStayOutOfTheHostsOutline()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "# Host\n\n![[source]]\n"),
            ("source.md", "# Embedded\n")).Render("page.md");

        Assert.Equal(["Host"], rendered.Outline.Select(h => h.Text));
    }
}
