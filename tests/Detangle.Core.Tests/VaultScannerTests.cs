using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for the directory walk that turns a folder into a vault snapshot.</summary>
public class VaultScannerTests
{
    [Fact]
    public void IndexesAttachmentsAlongsideMarkdown()
    {
        VaultSnapshot vault = FixtureVaults.Load("obsidian");

        Assert.Contains(vault.Documents, d => d.RelativePath == "attachments/diagram.png");
        Assert.False(vault.Documents.Single(d => d.RelativePath == "attachments/diagram.png").IsMarkdown);
    }

    [Fact]
    public void KeepsToolingDirectoriesOutOfTheDocumentSet()
    {
        VaultSnapshot vault = FixtureVaults.Load("obsidian");

        Assert.DoesNotContain(vault.Documents, d => d.RelativePath.StartsWith(".obsidian/", StringComparison.Ordinal));
    }

    [Fact]
    public void StillSeesMarkerFilesInsideIgnoredDirectories()
    {
        // ".obsidian" is never walked, but the flavor it identifies must still be found.
        Assert.Equal(VaultFlavor.Obsidian, FixtureVaults.Load("obsidian").Profile.Flavor);
        Assert.Equal(VaultFlavor.Foam, FixtureVaults.Load("foam").Profile.Flavor);
    }

    [Fact]
    public void UsesForwardSlashesRegardlessOfPlatform()
    {
        VaultSnapshot vault = FixtureVaults.Load("mkdocs");

        Assert.All(vault.Documents, d => Assert.DoesNotContain('\\', d.RelativePath));
    }

    [Fact]
    public void ParsesFrontmatterHeadingsAndLinksDuringTheScan()
    {
        VaultDocument document = FixtureVaults.Load("llm-wiki")
            .Index.ByRelativePath("wiki/concepts/attention-is-all-you-need.md").Single();

        Assert.Equal("Attention Is All You Need", document.Frontmatter.Title);
        Assert.Equal("concept", document.Frontmatter.Type);
        Assert.Equal("Attention Is All You Need", document.FirstHeading);
        Assert.Equal(2, document.Links.Count);
    }

    [Fact]
    public void DisplayNamePrefersTheFrontmatterTitle()
    {
        VaultDocument document = FixtureVaults.Load("dendron")
            .Index.ByRelativePath("lang.python.basics.md").Single();

        Assert.Equal("lang.python.basics", document.Stem);
        Assert.Equal("Basics", document.DisplayName);
    }

    [Fact]
    public void ScanningAMissingDirectoryThrows() =>
        Assert.Throws<DirectoryNotFoundException>(
            () => VaultScanner.Scan(Path.Combine(FixtureVaults.FixturesRoot, "vaults", "no-such-vault")));

    [Fact]
    public void AFixtureVaultScansWithoutDiagnostics() =>
        Assert.Empty(FixtureVaults.Load("torture").Diagnostics);
}
