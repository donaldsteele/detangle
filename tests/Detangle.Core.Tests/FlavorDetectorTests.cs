using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the format sniffer (plan.md section 5.7), including one pass per fixture
/// vault so that the thirteen surveyed formats are each identified from a real tree.
/// </summary>
public class FlavorDetectorTests
{
    public static TheoryData<string, VaultFlavor> FixtureFlavors
    {
        get
        {
            var data = new TheoryData<string, VaultFlavor>();

            data.Add("llm-wiki", VaultFlavor.LlmWiki);
            data.Add("obsidian", VaultFlavor.Obsidian);
            data.Add("foam", VaultFlavor.Foam);
            data.Add("dendron", VaultFlavor.Dendron);
            data.Add("logseq", VaultFlavor.Logseq);
            data.Add("quartz", VaultFlavor.Quartz);
            data.Add("zettelkasten", VaultFlavor.Zettelkasten);
            data.Add("mkdocs", VaultFlavor.MkDocs);
            data.Add("docusaurus", VaultFlavor.Docusaurus);
            data.Add("gitbook", VaultFlavor.GitBook);
            data.Add("docsify", VaultFlavor.Docsify);
            data.Add("mdbook", VaultFlavor.MdBook);
            data.Add("deepwiki", VaultFlavor.DeepWiki);
            data.Add("torture", VaultFlavor.Generic);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureFlavors))]
    public void IdentifiesEachFixtureVault(string vaultName, VaultFlavor expected) =>
        Assert.Equal(expected, FixtureVaults.Load(vaultName).Profile.Flavor);

    [Fact]
    public void EveryResearchedWikiFormatHasAFlavor()
    {
        // Thirteen surveyed formats plus Generic. If this count changes, plan.md
        // section 3.1 needs to change with it.
        Assert.Equal(14, Enum.GetValues<VaultFlavor>().Length);
    }

    [Fact]
    public void GenericIsTheDefaultFlavor() => Assert.Equal(VaultFlavor.Generic, default(VaultFlavor));

    [Fact]
    public void MarkerFilesInsideDirectoriesStillIdentifyTheVault() =>
        Assert.Equal(VaultFlavor.Obsidian, FlavorDetector.Detect([".obsidian/app.json", "note.md"]));

    [Fact]
    public void LlmWikiOutranksOtherMarkersItMayAlsoCarry()
    {
        // An LLM wiki is often kept in an Obsidian vault; the pair of markers is the more
        // specific signal, so it has to be tested before the single-file ones.
        VaultFlavor flavor = FlavorDetector.Detect(
            [".obsidian/app.json", "wiki/SCHEMA.md", "raw/paper.pdf", "wiki/index.md"]);

        Assert.Equal(VaultFlavor.LlmWiki, flavor);
    }

    [Fact]
    public void MdBookNeedsBothOfItsMarkers()
    {
        Assert.Equal(VaultFlavor.Generic, FlavorDetector.Detect(["book.toml", "notes.md"]));
        Assert.Equal(VaultFlavor.MdBook, FlavorDetector.Detect(["book.toml", "src/SUMMARY.md"]));
    }

    [Fact]
    public void ZettelkastenIsInferredFromFlatTimestampFilenames()
    {
        VaultFlavor flavor = FlavorDetector.Detect(
            ["202604201530-a.md", "202604201531-b.md", "202604201532-c.md"]);

        Assert.Equal(VaultFlavor.Zettelkasten, flavor);
    }

    [Fact]
    public void DendronIsInferredFromFlatDotHierarchies()
    {
        VaultFlavor flavor = FlavorDetector.Detect(
            ["lang.python.md", "lang.python.basics.md", "lang.rust.md", "lang.rust.traits.md"]);

        Assert.Equal(VaultFlavor.Dendron, flavor);
    }

    [Fact]
    public void ShapeBasedDetectionRequiresAFlatTree()
    {
        // A nested tree is evidence against both shape-based flavors, whose whole premise
        // is that the hierarchy lives in the filename instead of in folders.
        VaultFlavor flavor = FlavorDetector.Detect(
            ["a/lang.python.md", "b/lang.rust.md", "c/lang.go.md", "d/lang.zig.md"]);

        Assert.Equal(VaultFlavor.Generic, flavor);
    }

    [Fact]
    public void AProfileCanBeForcedOverTheSniffedResult()
    {
        VaultSnapshot vault = VaultScanner.Scan(
            FixtureVaults.PathTo("obsidian"),
            new VaultScanOptions { ForcedFlavor = VaultFlavor.Foam });

        Assert.Equal(VaultFlavor.Foam, vault.Profile.Flavor);
        Assert.True(vault.Profile.IsUserOverride);
    }

    [Fact]
    public void ProfilesCarryTheirFlavorSpecificRules()
    {
        Assert.True(VaultProfile.For(VaultFlavor.Dendron).PreferTitleAsDisplayName);
        Assert.True(VaultProfile.For(VaultFlavor.Dendron).DotPathHierarchy);
        Assert.True(VaultProfile.For(VaultFlavor.Logseq).DecodeLogseqFilenames);
        Assert.True(VaultProfile.For(VaultFlavor.Zettelkasten).IdentifierPrefixMatch);
        Assert.Equal("README.md", VaultProfile.For(VaultFlavor.GitBook).FolderIndexNames[0]);
        Assert.True(VaultProfile.For(VaultFlavor.Generic).IsEnabled(ResolutionRule.FuzzyNearest));
    }
}
