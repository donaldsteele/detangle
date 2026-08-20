using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for fragment matching, per plan.md section 5.6.</summary>
public class AnchorResolverTests
{
    private static readonly VaultDocument Host = TestVault.CreateDocument(
        "host.md",
        """
        # Host

        ## Punctuation: Don't, Really?

        Text with a marker. ^intro

        ## Duplicate

        First.

        ## Duplicate

        Second.

        ## Outer

        ### Inner

        - A block
          id:: 6512a0f1-1111-2222-3333-444455556666
        """);

    [Fact]
    public void MatchesRawHeadingTextCaseInsensitively()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, "punctuation: don't, really?", isBlockId: false);

        Assert.Equal(AnchorRule.RawHeading, anchor.Rule);
    }

    [Fact]
    public void MatchesGithubSluggerSlugs()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, "punctuation-dont-really", isBlockId: false);

        Assert.Equal(AnchorRule.HeadingSlug, anchor.Rule);
    }

    [Fact]
    public void MatchesDedupCountersOnDuplicateHeadings()
    {
        AnchorResolution first = AnchorResolver.Resolve(Host, "duplicate", isBlockId: false);
        AnchorResolution second = AnchorResolver.Resolve(Host, "duplicate-1", isBlockId: false);

        Assert.NotEqual(first.Line, second.Line);
    }

    [Fact]
    public void MatchesTheLastSegmentOfANestedHeadingPath()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, "Outer#Inner", isBlockId: false);

        Assert.Equal(AnchorRule.RawHeading, anchor.Rule);
        Assert.Equal(Host.Headings.Single(h => h.Text == "Inner").Line, anchor.Line);
    }

    [Fact]
    public void MatchesBlockMarkers()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, "intro", isBlockId: true);

        Assert.Equal(AnchorRule.BlockId, anchor.Rule);
    }

    [Fact]
    public void MatchesLogseqUuids()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(
            Host, "6512a0f1-1111-2222-3333-444455556666", isBlockId: true);

        Assert.Equal(AnchorRule.BlockUuid, anchor.Rule);
    }

    [Theory]
    [InlineData("L10", 10, null)]
    [InlineData("L10-L20", 10, 20)]
    public void ReadsCodeCitationsAsLineRangesRatherThanHeadings(string fragment, int start, int? end)
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, fragment, isBlockId: false);

        Assert.Equal(AnchorRule.LineRange, anchor.Rule);
        Assert.Equal(start, anchor.Line);
        Assert.Equal(end, anchor.EndLine);
    }

    [Theory]
    [InlineData("page=3")]
    [InlineData("height=400")]
    public void ReadsDocumentViewerParameters(string fragment) =>
        Assert.Equal(AnchorRule.DocumentParameter, AnchorResolver.Resolve(Host, fragment, false).Rule);

    [Fact]
    public void AnUnmatchedFragmentWarnsWithoutFailingTheLink()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, "no-such-heading", isBlockId: false);

        Assert.Equal(AnchorRule.Unresolved, anchor.Rule);
        Assert.False(anchor.IsResolved);
        Assert.Contains("host.md", anchor.Warning);
    }

    [Fact]
    public void NoFragmentIsNotAFailure()
    {
        AnchorResolution anchor = AnchorResolver.Resolve(Host, null, isBlockId: false);

        Assert.Equal(AnchorRule.None, anchor.Rule);
        Assert.False(anchor.IsResolved);
    }

    [Fact]
    public void AFailedAnchorStillNavigatesToTheFile()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[target#gone]]"),
            ("target.md", "# Target\n\n## Present\n"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal("target.md", resolution.Target?.RelativePath);
        Assert.Equal(AnchorRule.Unresolved, resolution.Anchor.Rule);
    }
}
