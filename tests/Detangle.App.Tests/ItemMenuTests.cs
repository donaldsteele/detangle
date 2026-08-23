using Avalonia.Controls;
using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Search;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the right-click menus and the capability object behind them.
/// <para>
/// The point of the capability object is that a command the head cannot run is never
/// offered, rather than being offered and then swallowing the exception — so the tests
/// worth having are about what the menu does and does not contain.
/// </para>
/// </summary>
public class ItemMenuTests
{
    [Fact]
    public void EverySortOfRowResolvesToThePageBehindIt()
    {
        VaultDocument document = TestDocument();

        Assert.Same(document, ItemMenus.DocumentOf(document));
        Assert.Same(document, ItemMenus.DocumentOf(new DocumentTab(document)));
        Assert.Same(document, ItemMenus.DocumentOf(new NavigationNode("A", document, [])));
        Assert.Same(document, ItemMenus.DocumentOf(new SearchHit(document, "H", "s", 1, 0)));
        Assert.Null(ItemMenus.DocumentOf(null));
        Assert.Null(ItemMenus.DocumentOf("not a row"));
    }

    [Fact]
    public void APageMenuOffersTheThreeWaysToQuoteIt()
    {
        IReadOnlyList<string> headers = Headers(HeadCapabilities.Desktop);

        Assert.Equal(
            ["Open", "Copy path", "Copy as wikilink", "Copy as markdown link", "Reveal in file manager"],
            headers);

        // Two separators is the cap, and the vault-relative path is the only one offered:
        // the absolute one names the reader's home directory.
        Assert.DoesNotContain(headers, h => h.Contains("absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AHeadWithNoFileManagerIsNeverOfferedOne()
    {
        Assert.DoesNotContain("Reveal in file manager", Headers(HeadCapabilities.Browser));

        // And the separator that introduced it goes with it, rather than leaving a rule
        // across the bottom of the menu with nothing under it.
        Assert.Single(Items(HeadCapabilities.Browser).OfType<Separator>());
    }

    [Fact]
    public void NothingInAMenuRenamesMovesOrDeletesAFile()
    {
        // There is no undo in the application yet, and a context menu is exactly where an
        // accidental click lands.
        string[] forbidden = ["rename", "delete", "move", "remove"];

        foreach (string header in Headers(HeadCapabilities.Desktop))
        {
            Assert.DoesNotContain(forbidden, word => header.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheDesktopStatesMoreThanTheBrowserCanDo()
    {
        Assert.True(HeadCapabilities.Desktop.CanRevealInFileManager);
        Assert.True(HeadCapabilities.Desktop.CanDropFolders);
        Assert.True(HeadCapabilities.Desktop.CanPersistAcrossSessions);

        Assert.False(HeadCapabilities.Browser.CanRevealInFileManager);
        Assert.False(HeadCapabilities.Browser.CanDropFolders);
        Assert.False(HeadCapabilities.Browser.CanPersistAcrossSessions);

        // A link out of the vault is a navigation rather than a process, so both heads do
        // it: the capability object states what each head wired up, not what platform it is.
        Assert.True(HeadCapabilities.Browser.CanOpenExternalLinks);
    }

    private static IReadOnlyList<Control> Items(HeadCapabilities capabilities) =>
        [.. ItemMenus.PageItems(TestDocument(), new ShellViewModel { Capabilities = capabilities }, view: null!)];

    private static IReadOnlyList<string> Headers(HeadCapabilities capabilities) =>
        [.. Items(capabilities).OfType<MenuItem>().Select(i => (string)i.Header!)];

    private static VaultDocument TestDocument() => new()
    {
        RelativePath = "wiki/a.md",
        AbsolutePath = "/vault/wiki/a.md",
        Stem = "a",
        Extension = ".md",
        DirectoryPath = "wiki",
    };
}
