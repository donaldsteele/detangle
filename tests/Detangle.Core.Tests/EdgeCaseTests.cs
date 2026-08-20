using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// One named test per entry in plan.md section 3.2 — "the 23 link edge cases that
/// actually bite". These are the phase 1 exit criteria: each case is listed in the
/// research because a real wiki viewer gets it wrong, so each one gets a test that says
/// out loud what Detangle does instead.
/// </summary>
public class EdgeCaseTests
{
    [Fact]
    public void Case01_CaseDrift()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[CASE-DRIFT]]"),
            ("case-drift.md", "# Case drift"));

        Assert.Equal(ResolutionRule.CaseInsensitiveStem, vault.ResolveOnly("source.md").Rule);
    }

    [Theory]
    [InlineData("[[separator drift]]")]
    [InlineData("[[separator_drift]]")]
    [InlineData("[[separator.drift]]")]
    [InlineData("[[Separator-Drift]]")]
    public void Case02_SeparatorDrift(string link)
    {
        TestVault vault = TestVault.Build(
            ("source.md", link),
            ("separator-drift.md", "# Separator drift"));

        Assert.Equal("separator-drift.md", vault.ResolveOnly("source.md").Target?.RelativePath);
    }

    [Theory]
    [InlineData("[[encoded%20target]]")]
    [InlineData("[[encoded%2520target]]")]
    [InlineData("[Encoded](encoded%20target.md)")]
    [InlineData("[[a%2Fb]]")]
    public void Case03_PercentEncodingIncludingDoubleEncoding(string link)
    {
        TestVault vault = TestVault.Build(
            ("source.md", link),
            ("encoded target.md", "# Encoded target"),
            ("a/b.md", "# Slash encoded"));

        Assert.NotNull(vault.ResolveOnly("source.md").Target);
    }

    [Theory]
    [InlineData("[[target]]")]
    [InlineData("[[target.md]]")]
    [InlineData("[[target.markdown]]")]
    [InlineData("[[target.html]]")]
    public void Case04_MissingOrWrongExtension(string link)
    {
        TestVault vault = TestVault.Build(
            ("source.md", link),
            ("target.md", "# Target"));

        Assert.Equal("target.md", vault.ResolveOnly("source.md").Target?.RelativePath);
    }

    [Fact]
    public void Case05_SlugifiedVersusRawTitlesInBothDirections()
    {
        TestVault slugToTitle = TestVault.Build(
            ("source.md", "[[attention-is-all-you-need]]"),
            ("Attention Is All You Need.md", "# Attention Is All You Need"));

        TestVault titleToSlug = TestVault.Build(
            ("source.md", "[[Attention Is All You Need]]"),
            ("attention-is-all-you-need.md", "# Attention Is All You Need"));

        Assert.NotNull(slugToTitle.ResolveOnly("source.md").Target);
        Assert.NotNull(titleToSlug.ResolveOnly("source.md").Target);
    }

    [Fact]
    public void Case06_AnchorsWithPunctuationAreDeletedNotReplaced()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[target#dont-really]]"),
            ("target.md", "# Target\n\n## Don't, Really?\n"));

        Assert.Equal(AnchorRule.HeadingSlug, vault.ResolveOnly("source.md").Anchor.Rule);
    }

    [Fact]
    public void Case07_DuplicateHeadingsGetCounters()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[target#duplicate-1]]"),
            ("target.md", "# Target\n\n## Duplicate\n\nFirst.\n\n## Duplicate\n\nSecond.\n"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(AnchorRule.HeadingSlug, resolution.Anchor.Rule);
        Assert.Equal(7, resolution.Anchor.Line);
    }

    [Fact]
    public void Case08_NestedHeadingPaths()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[target#Outer#Inner]]"),
            ("target.md", "# Target\n\n## Outer\n\n### Inner\n"));

        Assert.Equal(5, vault.ResolveOnly("source.md").Anchor.Line);
    }

    [Fact]
    public void Case09_BlockRefsAreMatchedBeforeHeadingAnchors()
    {
        // "#^id" has to be tested before "#", or the block reference parses as a heading
        // anchor whose text begins with a caret.
        LinkReference link = Assert.Single(
            DocumentParser.Parse("source.md", "[[target#^block-1]]").Links);

        Assert.True(link.AnchorIsBlockId);
        Assert.Equal("block-1", link.Anchor);
    }

    [Fact]
    public void Case10_LinksToFolders()
    {
        // The index's own heading deliberately differs from the folder name, so the
        // alias step cannot claim the link before the folder-index step sees it.
        TestVault vault = TestVault.Build(
            ("source.md", "[[guide]]"),
            ("guide/index.md", "# Everything you need to know"));

        Assert.Equal(ResolutionRule.FolderIndex, vault.ResolveOnly("source.md").Rule);
    }

    [Fact]
    public void Case11_AmbiguousBasenamesAcrossDirectories()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[note]]"),
            ("alpha/note.md", "# Alpha"),
            ("zebra/note.md", "# Zebra"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.True(resolution.IsAmbiguous);
        Assert.Equal("alpha/note.md", resolution.Target?.RelativePath);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Theory]
    [InlineData(@"[[notes\sub\page]]")]
    [InlineData("[[/notes/sub/page]]")]
    public void Case12_WindowsSeparatorsAndVaultAbsolutePaths(string link)
    {
        TestVault vault = TestVault.Build(
            ("source.md", link),
            ("notes/sub/page.md", "# Page"));

        Assert.Equal("notes/sub/page.md", vault.ResolveOnly("source.md").Target?.RelativePath);
    }

    [Fact]
    public void Case13_ThePipeOverloadIsDecidedByTargetExtension()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md", "![[diagram.png|300]] and [[note|a label]]\n");

        Assert.Equal("300", parsed.Links[0].SizeSpec);
        Assert.Null(parsed.Links[0].Label);
        Assert.Equal("a label", parsed.Links[1].Label);
        Assert.Null(parsed.Links[1].SizeSpec);
    }

    [Fact]
    public void Case14_EmbedsAreDistinguishedFromLinks()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "![[target]] and [[target]]\n");

        Assert.True(parsed.Links[0].IsEmbed);
        Assert.False(parsed.Links[1].IsEmbed);
    }

    [Fact]
    public void Case15_EmptyAndSelfTargets()
    {
        ParsedDocument parsed = DocumentParser.Parse("source.md", "[[#Heading]] [[|alias]] [[]]\n");

        LinkReference only = Assert.Single(parsed.Links);

        Assert.True(only.IsSelfReference);
        Assert.Equal("Heading", only.Anchor);
    }

    [Fact]
    public void Case16_EscapedCodeAndCommentedLinksAreExcludedFromTheGraph()
    {
        ParsedDocument parsed = DocumentParser.Parse(
            "source.md",
            """
            `[[inline]]`

            \[\[escaped\]\]

            <!-- [[commented]] -->

            ```md
            [[fenced]]
            ```

                [[indented]]

            [[real]]
            """);

        Assert.Equal("real", Assert.Single(parsed.Links).RawTarget);
    }

    [Fact]
    public void Case17_TrailingPunctuationIsNotPartOfTheTarget()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "See [[target]]., and [[target]]!\n"),
            ("target.md", "# Target"));

        Assert.All(vault.ResolveLinksOf("source.md"), r => Assert.Equal("target.md", r.Target?.RelativePath));
    }

    [Fact]
    public void Case18_UnicodeNfcVersusNfd()
    {
        // macOS stores filenames decomposed; a link typed on any other platform arrives
        // composed. Without folding, the two never meet.
        TestVault vault = TestVault.Build(
            ("source.md", "[[café]]"),
            ("café.md", "# Cafe"));

        Assert.NotNull(vault.ResolveOnly("source.md").Target);
    }

    [Fact]
    public void Case19_DendronDotNamesAreNeverTreatedAsExtensions()
    {
        VaultDocument document = TestVault.CreateDocument("lang.python.basics.md", "# Basics");

        Assert.Equal("lang.python.basics", document.Stem);

        TestVault vault = TestVault.Build(
            VaultFlavor.Dendron,
            ("lang.python.md", "[[lang.python.basics]]"),
            ("lang.python.basics.md", "# Basics"));

        Assert.Equal(
            "lang.python.basics.md",
            vault.ResolveOnly("lang.python.md").Target?.RelativePath);
    }

    [Fact]
    public void Case20_MkDocsUseDirectoryUrlsOffByOne()
    {
        // With use_directory_urls on, "../guide/" from a page that is served as a
        // directory resolves one level shallower than the file layout suggests. Both the
        // file-shaped and the directory-shaped link have to reach the same page.
        TestVault vault = TestVault.Build(
            VaultFlavor.MkDocs,
            ("docs/guide/deep-dive.md", "[Up](../guide/index.md) and [Section](../guide)"),
            ("docs/guide/index.md", "# Guide"));

        Assert.All(
            vault.ResolveLinksOf("docs/guide/deep-dive.md"),
            r => Assert.Equal("docs/guide/index.md", r.Target?.RelativePath));
    }

    [Fact]
    public void Case21_LineRangeCitesAreNotWikiPages()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[resolver](src/resolver.py#L10-L20)"),
            ("src/resolver.py", "def resolve():\n    return None\n"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(AnchorRule.LineRange, resolution.Anchor.Rule);
        Assert.Equal(10, resolution.Anchor.Line);
        Assert.Equal(20, resolution.Anchor.EndLine);
    }

    [Fact]
    public void Case22_NotesIndexVersusNotesMdBothClaimTheFolderIndex()
    {
        // Both files can legitimately claim to be the index of "notes". The exact path
        // wins, deterministically, and the loser is still reachable by its own name.
        TestVault vault = TestVault.Build(
            ("source.md", "[[notes]]"),
            ("notes.md", "# Notes sibling"),
            ("notes/index.md", "# Notes index"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.ExactVaultPath, resolution.Rule);
        Assert.Equal("notes.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void Case23_CaseOnlyDuplicateFiles()
    {
        // Two files differing only in case cannot coexist on Windows or macOS, but they
        // can in a repository cloned on Linux — and then the case-sensitive step is the
        // only one that can tell them apart.
        TestVault vault = TestVault.Build(
            ("upper.md", "[[Note]]"),
            ("lower.md", "[[note]]"),
            ("Note.md", "# Upper"),
            ("note.md", "# Lower"));

        Assert.Equal("Note.md", vault.ResolveOnly("upper.md").Target?.RelativePath);
        Assert.Equal("note.md", vault.ResolveOnly("lower.md").Target?.RelativePath);
        Assert.False(vault.ResolveOnly("upper.md").IsAmbiguous);
    }
}
