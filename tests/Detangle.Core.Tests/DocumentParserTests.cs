using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for link, heading and block-anchor extraction — in particular for what must
/// <em>not</em> be extracted, which is where every wiki viewer's graph goes wrong.
/// </summary>
public class DocumentParserTests
{
    [Fact]
    public void ExtractsWikiLinkPartsIncludingAliasAndAnchor()
    {
        LinkReference link = ParseOne("[[notes/target#Section|A label]]");

        Assert.Equal(LinkSyntax.WikiLink, link.Syntax);
        Assert.Equal("notes/target", link.RawTarget);
        Assert.Equal("A label", link.Label);
        Assert.Equal("Section", link.Anchor);
        Assert.False(link.AnchorIsBlockId);
        Assert.False(link.IsEmbed);
    }

    [Fact]
    public void ReadsBlockAnchorsBeforeHeadingAnchors()
    {
        LinkReference link = ParseOne("[[target#^block-1]]");

        Assert.Equal("target", link.RawTarget);
        Assert.Equal("block-1", link.Anchor);
        Assert.True(link.AnchorIsBlockId);
    }

    [Fact]
    public void KeepsNestedHeadingPathsIntact()
    {
        Assert.Equal("Outer#Inner", ParseOne("[[target#Outer#Inner]]").Anchor);
    }

    [Fact]
    public void ReadsEmbeds()
    {
        LinkReference link = ParseOne("![[notes/target]]");

        Assert.True(link.IsEmbed);
        Assert.Equal("notes/target", link.RawTarget);
    }

    [Theory]
    [InlineData("![[diagram.png|300]]", "300", null)]
    [InlineData("![[diagram.png|300x200]]", "300x200", null)]
    [InlineData("![[note.md|A label]]", null, "A label")]
    [InlineData("[[note|A label]]", null, "A label")]
    public void DisambiguatesThePipeOverloadByTargetExtension(string markdown, string? size, string? label)
    {
        LinkReference link = ParseOne(markdown);

        Assert.Equal(size, link.SizeSpec);
        Assert.Equal(label, link.Label);
    }

    [Fact]
    public void ReadsMarkdownLinksAndTheirLabels()
    {
        LinkReference link = ParseOne("[The label](notes/target.md#section)");

        Assert.Equal(LinkSyntax.Markdown, link.Syntax);
        Assert.Equal("notes/target.md", link.RawTarget);
        Assert.Equal("The label", link.Label);
        Assert.Equal("section", link.Anchor);
    }

    [Theory]
    [InlineData("`[[inline-code]]`")]
    [InlineData("\\[\\[escaped\\]\\]")]
    [InlineData("<!-- [[commented-out]] -->")]
    [InlineData("```\n[[fenced]]\n```")]
    [InlineData("    [[indented-code]]")]
    public void ExcludesLinksThatAreNotLinks(string markdown)
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", markdown);

        Assert.Empty(parsed.Links);
    }

    [Fact]
    public void ExcludesLinksInsideFrontmatterStringsButKeepsReferenceKeys()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md",
            "---\ntitle: \"[[not-a-link]]\"\nrelated:\n  - a-real-reference\n---\n\n# Body\n");

        LinkReference link = Assert.Single(parsed.Links);

        Assert.Equal(LinkSyntax.Frontmatter, link.Syntax);
        Assert.Equal("a-real-reference", link.RawTarget);
    }

    [Fact]
    public void UnwrapsWikiLinkSyntaxInsideReferenceKeys()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md", "---\nsources:\n  - \"[[a-source|labelled]]\"\n---\n");

        Assert.Equal("a-source", Assert.Single(parsed.Links).RawTarget);
    }

    [Fact]
    public void DropsEmptyAndAliasOnlyTargets()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "[[]] and [[|just-an-alias]]\n");

        Assert.Empty(parsed.Links);
    }

    [Fact]
    public void KeepsSelfReferencingAnchors()
    {
        LinkReference link = ParseOne("[[#A Heading]]");

        Assert.True(link.IsSelfReference);
        Assert.Equal("A Heading", link.Anchor);
    }

    [Fact]
    public void ReadsLogseqBlockReferencesAndTags()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md", "- A block ((6512a0f1-1111-2222-3333-444455556666)) and #a-tag\n");

        Assert.Equal(
            [LinkSyntax.BlockReference, LinkSyntax.Tag],
            parsed.Links.Select(l => l.Syntax));
    }

    [Fact]
    public void IgnoresNumericHashesThatAreNotTags()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "Issue #1234 and release #2026\n");

        Assert.Empty(parsed.Links);
    }

    [Fact]
    public void NumbersHeadingsWithDedupCountersInDocumentOrder()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md", "# Title\n\n## Duplicate\n\n## Duplicate\n\n## Duplicate\n");

        Assert.Equal(
            ["title", "duplicate", "duplicate-1", "duplicate-2"],
            parsed.Headings.Select(h => h.Slug));
    }

    [Fact]
    public void ReadsBlockMarkersAndLogseqIds()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md",
            "A paragraph. ^intro\n\n- A block\n  id:: 6512a0f1-1111-2222-3333-444455556666\n");

        Assert.Equal(["intro", "6512a0f1-1111-2222-3333-444455556666"], parsed.BlockAnchors.Select(a => a.Id));
        Assert.Equal([false, true], parsed.BlockAnchors.Select(a => a.IsUuid));
    }

    [Fact]
    public void IgnoresBlockMarkersInsideCodeFences()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "```sh\ngit rev-parse ^intro\n```\n");

        Assert.Empty(parsed.BlockAnchors);
    }

    [Fact]
    public void ReportsLineNumbersThatSurviveFrontmatter()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md", "---\ntitle: A\n---\n\n# Heading\n\nBody with [[a-link]].\n");

        Assert.Equal(5, parsed.Headings.Single().Line);
        Assert.Equal(7, parsed.Links.Single(l => l.Syntax == LinkSyntax.WikiLink).Line);
    }

    [Fact]
    public void ParsesCarriageReturnsWithoutShiftingPositions()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "# Heading\r\n\r\n[[a-link]]\r\n");

        Assert.Equal(3, parsed.Links.Single().Line);
    }

    private static LinkReference ParseOne(string markdown) =>
        Assert.Single(DocumentParser.Parse("source.md", markdown).Links);
}
