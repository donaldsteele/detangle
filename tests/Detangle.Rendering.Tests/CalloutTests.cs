using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for both callout dialects (plan.md section 6.1). A generated wiki mixes them,
/// often on the same page, so neither can be treated as the exotic one.
/// </summary>
public class CalloutTests
{
    [Fact]
    public void ReadsAnObsidianCallout()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("> [!note]\n> The body.\n")[0]);

        Assert.Equal("note", callout.Kind);
        Assert.Equal("Note", callout.Title);
        Assert.Equal(CalloutDialect.Obsidian, callout.Dialect);
        Assert.False(callout.IsCollapsible);
        Assert.Equal("The body.", RenderModelBuilder.ToPlainText(
            Assert.IsType<ParagraphRenderBlock>(callout.Blocks[0]).Inlines));
    }

    [Fact]
    public void ReadsAnObsidianCalloutTitle()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("> [!warning] Mind the gap\n> The body.\n")[0]);

        Assert.Equal("warning", callout.Kind);
        Assert.Equal("Mind the gap", callout.Title);
    }

    [Theory]
    [InlineData("> [!tip]+ Open\n> Body.\n", true, false)]
    [InlineData("> [!tip]- Closed\n> Body.\n", true, true)]
    [InlineData("> [!tip] Plain\n> Body.\n", false, false)]
    public void ReadsObsidianFoldMarkers(string markdown, bool collapsible, bool collapsed)
    {
        var callout = Assert.IsType<CalloutRenderBlock>(RenderTestVault.BodyOf(markdown)[0]);

        Assert.Equal(collapsible, callout.IsCollapsible);
        Assert.Equal(collapsed, callout.StartsCollapsed);
    }

    [Fact]
    public void AnOrdinaryBlockquoteStaysAQuote()
    {
        Assert.IsType<QuoteRenderBlock>(RenderTestVault.BodyOf("> Just a quotation.\n")[0]);
    }

    [Fact]
    public void ABracketedLineThatIsNotACalloutStaysAQuote()
    {
        Assert.IsType<QuoteRenderBlock>(RenderTestVault.BodyOf("> [not a callout] text\n")[0]);
    }

    [Fact]
    public void ReadsAMkDocsAdmonition()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("!!! note\n    The body.\n")[0]);

        Assert.Equal("note", callout.Kind);
        Assert.Equal("Note", callout.Title);
        Assert.Equal(CalloutDialect.MkDocs, callout.Dialect);
        Assert.False(callout.IsCollapsible);
    }

    [Fact]
    public void ReadsAMkDocsAdmonitionTitle()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("!!! warning \"Mind the gap\"\n    The body.\n")[0]);

        Assert.Equal("warning", callout.Kind);
        Assert.Equal("Mind the gap", callout.Title);
    }

    [Theory]
    [InlineData("??? tip\n    Body.\n", true, true)]
    [InlineData("???+ tip\n    Body.\n", true, false)]
    [InlineData("!!! tip\n    Body.\n", false, false)]
    public void ReadsMkDocsCollapsibleMarkers(string markdown, bool collapsible, bool collapsed)
    {
        var callout = Assert.IsType<CalloutRenderBlock>(RenderTestVault.BodyOf(markdown)[0]);

        Assert.Equal(collapsible, callout.IsCollapsible);
        Assert.Equal(collapsed, callout.StartsCollapsed);
    }

    [Fact]
    public void AnAdmonitionEndsAtTheFirstUnindentedLine()
    {
        IReadOnlyList<RenderBlock> blocks = RenderTestVault.BodyOf(
            "!!! note\n    Inside.\n\nOutside.\n");

        var callout = Assert.IsType<CalloutRenderBlock>(blocks[0]);

        Assert.Equal("Inside.", RenderModelBuilder.ToPlainText(
            Assert.IsType<ParagraphRenderBlock>(callout.Blocks[0]).Inlines));
        Assert.Equal("Outside.", RenderModelBuilder.ToPlainText(
            Assert.IsType<ParagraphRenderBlock>(blocks[1]).Inlines));
    }

    [Fact]
    public void AnAdmonitionKeepsMultipleParagraphsAcrossBlankLines()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("!!! note\n    First.\n\n    Second.\n")[0]);

        Assert.Equal(2, callout.Blocks.Count);
    }

    [Fact]
    public void AdmonitionBodiesGetFullMarkdown()
    {
        var callout = Assert.IsType<CalloutRenderBlock>(
            RenderTestVault.BodyOf("!!! note\n    - one\n    - two\n")[0]);

        Assert.Equal(2, Assert.IsType<ListRenderBlock>(callout.Blocks[0]).Items.Count);
    }

    [Fact]
    public void LinksInsideCalloutsResolveLikeAnyOther()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "> [!note]\n> See [[My Target]].\n"),
            ("my-target.md", "# My Target")).Render("page.md");

        Assert.Equal("my-target.md", Assert.Single(rendered.Resolutions).Target?.RelativePath);
    }

    [Fact]
    public void ThreeExclamationsThatAreNotAnAdmonitionStayText()
    {
        Assert.IsType<ParagraphRenderBlock>(RenderTestVault.BodyOf("!!!\n")[0]);
        Assert.IsType<ParagraphRenderBlock>(RenderTestVault.BodyOf("Wow!!! Really.\n")[0]);
    }
}
