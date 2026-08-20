using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// One test per step of the chain in plan.md section 5.3, each written so that the step
/// under test is the only one that can fire. Together with the golden files these are
/// what keep the step ordering honest.
/// </summary>
public class ResolutionChainTests
{
    [Fact]
    public void Step1_ExactVaultPath()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[notes/target]]"),
            ("notes/target.md", "# Target"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.ExactVaultPath, resolution.Rule);
        Assert.Equal("notes/target.md", resolution.Target?.RelativePath);
        Assert.Equal(ResolutionConfidence.Exact, resolution.Confidence);
    }

    [Fact]
    public void Step2_NoteRelativePath()
    {
        TestVault vault = TestVault.Build(
            ("a/source.md", "[[../b/target]]"),
            ("b/target.md", "# Target"));

        LinkResolution resolution = vault.ResolveOnly("a/source.md");

        Assert.Equal(ResolutionRule.NoteRelativePath, resolution.Rule);
        Assert.Equal("b/target.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void Step2_RefusesToClimbAboveTheVaultRoot()
    {
        // "../" from the root would leave the vault. The step declines rather than
        // clamping to the root, so the link falls through to the name-based steps.
        TestVault vault = TestVault.Build(
            ("source.md", "[[../outside]]"),
            ("outside.md", "# Reached by name, not by path"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.CaseSensitiveStem, resolution.Rule);
    }

    [Fact]
    public void Step3_CaseSensitiveStem()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[Target]]"),
            ("deep/nested/Target.md", "# Target"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.CaseSensitiveStem, resolution.Rule);
    }

    [Fact]
    public void Step4_PathSuffixMatchesTrailingSegmentsOnly()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[reference/note]]"),
            ("deep/reference/note.md", "# Note"),
            ("other/note.md", "# Other note"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.PathSuffix, resolution.Rule);
        Assert.Equal("deep/reference/note.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void Step4_DoesNotFireForABareName()
    {
        // A bare name has no path structure to match, so the suffix rule must stand aside
        // and let the case and normalization steps report what actually drifted.
        TestVault vault = TestVault.Build(
            ("source.md", "[[NOTE]]"),
            ("deep/note.md", "# Note"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.CaseInsensitiveStem, resolution.Rule);
    }

    [Fact]
    public void Step5_CaseInsensitiveStem()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[TARGET]]"),
            ("target.md", "# Target"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.CaseInsensitiveStem, resolution.Rule);
        Assert.Equal(ResolutionConfidence.Normalized, resolution.Confidence);
    }

    [Fact]
    public void Step6_NormalizedName()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[My Note_Title]]"),
            ("my-note-title.md", "# My Note Title"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.NormalizedName, resolution.Rule);
    }

    [Fact]
    public void Step7_AliasTitleOrFirstHeading()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[The Long Name]]"),
            ("short.md", "---\naliases: [The Long Name]\n---\n\n# Short"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.Alias, resolution.Rule);
    }

    [Fact]
    public void Step8_FrontmatterIdentifier()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[note-id-42]]"),
            ("unrelated-filename.md", "---\nid: note-id-42\n---\n\n# Something else"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.Identifier, resolution.Rule);
    }

    [Fact]
    public void Step8_ZettelPrefixMatch()
    {
        TestVault vault = TestVault.Build(
            VaultFlavor.Zettelkasten,
            ("202604201530-source.md", "[[202604201531]]"),
            ("202604201531-on-context.md", "# On context"));

        LinkResolution resolution = vault.ResolveOnly("202604201530-source.md");

        Assert.Equal(ResolutionRule.Identifier, resolution.Rule);
        Assert.Equal("202604201531-on-context.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void Step9_FolderIndex()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[guide]]"),
            ("guide/index.md", "# Guide index"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.FolderIndex, resolution.Rule);
        Assert.Equal("guide/index.md", resolution.Target?.RelativePath);
        Assert.Equal(ResolutionConfidence.Heuristic, resolution.Confidence);
    }

    [Fact]
    public void Step9_FolderIndexPrecedenceFollowsTheProfile()
    {
        // GitBook's index is README.md; the generic profile prefers index.md. Same files,
        // different answer, and that is the entire point of the profile.
        (string, string)[] files =
        [
            ("source.md", "[[chapter]]"),
            ("chapter/index.md", "# Index"),
            ("chapter/README.md", "# Readme"),
        ];

        Assert.Equal(
            "chapter/index.md",
            TestVault.Build(VaultFlavor.Generic, files).ResolveOnly("source.md").Target?.RelativePath);

        Assert.Equal(
            "chapter/README.md",
            TestVault.Build(VaultFlavor.GitBook, files).ResolveOnly("source.md").Target?.RelativePath);
    }

    [Fact]
    public void Step10_LogseqEncodingVariant()
    {
        TestVault vault = TestVault.Build(
            VaultFlavor.Logseq,
            ("pages/source.md", "[[projects/detangle]]"),
            ("pages/projects___detangle.md", "# Detangle"));

        LinkResolution resolution = vault.ResolveOnly("pages/source.md");

        Assert.Equal(ResolutionRule.EncodingVariant, resolution.Rule);
    }

    [Fact]
    public void Step11_ExtensionProbeFindsAttachmentsAnywhere()
    {
        TestVault vault = TestVault.Build(
            ("deep/source.md", "![[diagram.png]]"),
            ("attachments/diagram.png", string.Empty));

        LinkResolution resolution = vault.ResolveOnly("deep/source.md");

        Assert.Equal(ResolutionRule.ExtensionProbe, resolution.Rule);
        Assert.True(resolution.Link.IsEmbed);
    }

    [Fact]
    public void Step12_FuzzyMatchesAreSuggestedButNeverNavigatedTo()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[attentin]]"),
            ("attention.md", "# Attention"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.Placeholder, resolution.Rule);
        Assert.Null(resolution.Target);
        Assert.Equal("attention.md", Assert.Single(resolution.Suggestions).RelativePath);
    }

    [Fact]
    public void Step13_PlaceholderWhenNothingMatches()
    {
        TestVault vault = TestVault.Build(("source.md", "[[nothing-like-this-exists]]"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.True(resolution.IsUnresolved);
        Assert.Empty(resolution.Suggestions);
        Assert.Equal(ResolutionConfidence.Unresolved, resolution.Confidence);
    }

    [Fact]
    public void AmbiguityPicksShortestPathThenAlphabeticalAndKeepsEveryCandidate()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[note]]"),
            ("zebra/note.md", "# Zebra"),
            ("alpha/note.md", "# Alpha"),
            ("deep/deeper/note.md", "# Deep"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.True(resolution.IsAmbiguous);
        Assert.Equal("alpha/note.md", resolution.Target?.RelativePath);
        Assert.Equal(3, resolution.Candidates.Count);
    }

    [Fact]
    public void AnAmbiguousStepYieldsToALaterStepThatNamesExactlyOneDocument()
    {
        // Two files share the stem "note", so step 3 is ambiguous — but one of them
        // carries the alias being linked, and a single exact answer beats a coin flip.
        TestVault vault = TestVault.Build(
            ("source.md", "[[note]]"),
            ("a/note.md", "# A"),
            ("b/note.md", "# B"),
            ("c/aliased.md", "---\naliases: [note]\n---\n\n# Aliased"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.Alias, resolution.Rule);
        Assert.Equal("c/aliased.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void ARememberedChoiceOutranksTheWholeChain()
    {
        TestVault plain = TestVault.Build(
            ("source.md", "[[note]]"),
            ("alpha/note.md", "# Alpha"),
            ("zebra/note.md", "# Zebra"));

        var resolver = new LinkResolver(
            plain.Index,
            plain.Profile,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LinkResolver.ChoiceKey(string.Empty, "note")] = "zebra/note.md",
            });

        VaultDocument source = plain.Index.ByRelativePath("source.md").Single();
        LinkResolution resolution = resolver.ResolveAll(source).Single();

        Assert.Equal(ResolutionRule.RememberedChoice, resolution.Rule);
        Assert.Equal("zebra/note.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void ExternalTargetsAreNeverResolved()
    {
        TestVault vault = TestVault.Build(("source.md", "[Site](https://example.com/page)"));

        LinkResolution resolution = vault.ResolveOnly("source.md");

        Assert.Equal(ResolutionRule.NotAttempted, resolution.Rule);
        Assert.Equal("not a vault link", resolution.Explain());
    }

    [Fact]
    public void ExplainNamesTheRuleThatFired()
    {
        TestVault vault = TestVault.Build(
            ("source.md", "[[My Note]]"),
            ("my-note.md", "# My Note"));

        Assert.Equal(
            "resolved by normalized-name match to my-note.md",
            vault.ResolveOnly("source.md").Explain());
    }
}
