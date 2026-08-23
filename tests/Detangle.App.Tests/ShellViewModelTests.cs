using Detangle.Core.Graph;
using Detangle.Core.Vault;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the reader shell: tabs, history, panes, palette and theming. The shell is
/// deliberately free of Avalonia types so this suite needs no UI platform — the window
/// only wires these decisions to controls.
/// </summary>
public class ShellViewModelTests
{
    [Fact]
    public void OpeningAVaultBuildsNavigationTagsAndTheGraph()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        Assert.True(shell.HasVault);
        Assert.NotEmpty(shell.Navigation);
        Assert.NotEmpty(shell.Tags);
        Assert.NotNull(shell.Graph);
        Assert.Equal("IndexPage", shell.NavigationSourceName);
    }

    [Fact]
    public void OpeningAVaultLandsOnTheFirstNavigatedPage()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        Assert.NotNull(shell.ActiveTab);
        Assert.Equal("wiki/index.md", shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void AMissingVaultReportsItselfWithoutThrowing()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(Path.Combine(FixtureRoot, "vaults", "no-such-vault"));

        Assert.False(shell.HasVault);
        Assert.Contains("not found", shell.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|not<a>path")]
    public void AnUnusablePathReportsItselfWithoutThrowing(string path)
    {
        // The toolbar hands whatever is in the path box straight to this, and an empty box
        // is the state the application starts in — so the first thing a new user can do is
        // press the button with nothing typed. Path.GetFullPath throws ArgumentException
        // for that, which is not one of the filesystem exceptions the open path guards, and
        // it took the whole window down.
        var shell = new ShellViewModel();

        shell.OpenVault(path);

        Assert.False(shell.HasVault);
        Assert.NotEmpty(shell.Status);
    }

    [Fact]
    public void TheSidePanelsCollapseIndependently()
    {
        var shell = new ShellViewModel();

        Assert.True(shell.IsLeftPanelVisible);
        Assert.True(shell.IsRightPanelVisible);

        shell.ToggleLeftPanelCommand.Execute(null);

        Assert.False(shell.IsLeftPanelVisible);
        Assert.True(shell.IsRightPanelVisible);

        shell.ToggleRightPanelCommand.Execute(null);

        Assert.False(shell.IsLeftPanelVisible);
        Assert.False(shell.IsRightPanelVisible);
    }

    [Fact]
    public void TheStatusLineCountsBrokenAndAmbiguousLinks()
    {
        ShellViewModel shell = OpenVault("torture");

        Assert.Contains("broken", shell.LinkSummary, StringComparison.Ordinal);
        Assert.Contains("ambiguous", shell.LinkSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningTheSameDocumentTwiceReusesItsTab()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        VaultDocument document = Document(shell, "wiki/entities/vaswani-ashish.md");

        shell.Open(document);
        int after = shell.Tabs.Count;
        shell.Open(document);

        Assert.Equal(after, shell.Tabs.Count);
    }

    [Fact]
    public void ClosingATabActivatesItsNeighbour()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.Open(Document(shell, "wiki/entities/vaswani-ashish.md"));
        shell.Open(Document(shell, "wiki/concepts/attention-is-all-you-need.md"));

        DocumentTab last = shell.Tabs[^1];
        shell.Close(last);

        Assert.DoesNotContain(last, shell.Tabs);
        Assert.NotNull(shell.ActiveTab);
    }

    [Fact]
    public void HistoryGoesBackAndForward()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        string first = shell.ActiveTab!.Document.RelativePath;

        shell.Open(Document(shell, "wiki/entities/vaswani-ashish.md"));
        Assert.True(shell.CanGoBack);

        shell.GoBack();
        Assert.Equal(first, shell.ActiveTab!.Document.RelativePath);
        Assert.True(shell.CanGoForward);

        shell.GoForward();
        Assert.Equal("wiki/entities/vaswani-ashish.md", shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void GoingBackDoesNotItselfBecomeHistory()
    {
        // Otherwise back-and-forward would ping-pong between two pages forever.
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.Open(Document(shell, "wiki/entities/vaswani-ashish.md"));
        shell.GoBack();

        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void TheOutlineAndBacklinksFollowTheActiveTab()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.Open(Document(shell, "wiki/entities/vaswani-ashish.md"));

        Assert.Contains(shell.Outline, h => h.Text == "Vaswani, Ashish");
        Assert.NotEmpty(shell.Backlinks);
        Assert.All(shell.Backlinks, b => Assert.NotEqual(
            "wiki/entities/vaswani-ashish.md", b.Source.RelativePath));
    }

    [Fact]
    public void BacklinksNameTheRuleThatResolvedThem()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.Open(Document(shell, "wiki/concepts/attention-is-all-you-need.md"));

        // The index links to this page twice — once by title, once by path — and both
        // are backlinks, each labelled with the rule that got it there.
        Assert.Contains(
            shell.Backlinks,
            b => b.Source.RelativePath == "wiki/index.md"
                && b.Resolution.Rule == Core.Linking.ResolutionRule.NormalizedName);
        Assert.Contains(
            shell.Backlinks,
            b => b.Source.RelativePath == "wiki/index.md"
                && b.Resolution.Rule == Core.Linking.ResolutionRule.NoteRelativePath);
    }

    [Fact]
    public void FollowingALinkOpensItsTarget()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        RenderDocument rendered = shell.ActiveTab!.Rendered!;
        Core.Linking.LinkResolution resolution = rendered.Resolutions
            .First(r => r.Target?.RelativePath == "wiki/entities/vaswani-ashish.md");

        shell.Follow(resolution);

        Assert.Equal("wiki/entities/vaswani-ashish.md", shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void ThePaletteFindsPagesAndTheActiveDocumentsHeadings()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.TogglePalette();
        Assert.True(shell.IsPaletteOpen);
        Assert.NotEmpty(shell.PaletteResults);

        shell.PaletteQuery = "vaswani";

        Assert.All(
            shell.PaletteResults,
            entry => Assert.Contains("vaswani", entry.Title + entry.Subtitle, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThePaletteOpensWhatItFinds()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.TogglePalette();
        shell.PaletteQuery = "vaswani";
        shell.PaletteResults[0].Invoke();

        Assert.False(shell.IsPaletteOpen);
        Assert.Equal("wiki/entities/vaswani-ashish.md", shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void SwitchingThemeRerendersEveryOpenTab()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.Open(Document(shell, "wiki/entities/vaswani-ashish.md"));
        RenderDocument before = shell.Tabs[0].Rendered!;

        shell.ToggleTheme();

        Assert.True(shell.IsDarkTheme);
        Assert.All(shell.Tabs, tab => Assert.NotNull(tab.Rendered));
        Assert.NotSame(before, shell.Tabs[0].Rendered);
    }

    [Fact]
    public void ReadingPositionsAreRememberedPerDocument()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.RememberPosition("wiki/index.md", 420);

        Assert.Equal(420, shell.PositionOf("wiki/index.md"));
        Assert.Equal(0, shell.PositionOf("wiki/entities/vaswani-ashish.md"));
    }

    [Fact]
    public void TagsAreNestedByTheirSlashes()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        Assert.Contains(shell.Tags, t => t.Segment == "transformers");
    }

    [Fact]
    public void MentionsAreOfferedForPagesThatNameTheActiveOne()
    {
        ShellViewModel shell = OpenVault("torture");

        shell.Open(Document(shell, "case-drift.md"));

        // Nothing here should throw or hang; an empty list is a fine answer.
        Assert.NotNull(shell.Mentions);
    }

    [Fact]
    public void SelectingATagListsEveryPageItNames()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        TagNode tag = shell.Tags[0];

        shell.SelectTag(tag);

        // The tree's count is the promise the list has to keep.
        Assert.Equal(tag.TotalCount, shell.TagDocuments.Count);
        Assert.Equal(tag.FullTag, shell.SelectedTag);
        Assert.Contains(tag.FullTag.ToUpperInvariant(), shell.TagSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingTheTagSelectionEmptiesTheList()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.SelectTag(shell.Tags[0]);
        shell.SelectTag(null);

        Assert.Empty(shell.TagDocuments);
        Assert.Empty(shell.SelectedTag);
        Assert.Empty(shell.TagSummary);
    }

    [Fact]
    public void ATagChipSelectsItsTagAndOpensTheRail()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.IsLeftPanelVisible = false;

        Assert.True(shell.SelectTagNamed($"#{shell.Tags[0].FullTag}"));
        Assert.True(shell.IsLeftPanelVisible);
        Assert.NotEmpty(shell.TagDocuments);

        Assert.False(shell.SelectTagNamed("no-such-tag"));
    }

    [Fact]
    public void TheTagHeaderHandsTheSameQuestionToTheSearchBox()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.SelectTag(shell.Tags[0]);
        shell.SearchSelectedTag();

        Assert.Equal($"tag:{shell.Tags[0].FullTag}", shell.SearchQuery);
    }

    [Fact]
    public void NarrowingTheDoctorToOnePageLeavesTheVaultWideFindingsAlone()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        int everything = shell.Findings.Count;
        string page = shell.Findings[0].Document.RelativePath;

        shell.FilterFindings(page);

        // The tree narrows; the collection every other reader uses — the fix-all pass, the
        // report — still describes the whole vault.
        Assert.Equal(everything, shell.Findings.Count);
        Assert.All(
            shell.FindingGroups.SelectMany(g => g.Findings),
            f => Assert.Equal(page, f.Document.RelativePath));

        Assert.NotNull(shell.FindingFilterSummary);
        Assert.Contains(page, shell.FindingFilterSummary!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilterChipDismissesBackToTheWholeVault()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.FilterFindings(shell.Findings[0].Document.RelativePath);
        shell.FilterFindings(null);

        Assert.Null(shell.FindingFilterSummary);
        Assert.Null(shell.FindingPageFilter);
        Assert.Equal(
            shell.Findings.Count,
            shell.FindingGroups.Sum(g => g.Findings.Count));
    }

    [Fact]
    public void OpeningAnotherVaultForgetsAFilterAboutTheLastOne()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.FilterFindings("wiki/index.md");
        shell.OpenVault(Path.Combine(FixtureRoot, "vaults", "obsidian"));

        // A filter naming a page that is not in this vault would hide everything in it.
        Assert.Null(shell.FindingPageFilter);
        Assert.Null(shell.FindingFilterSummary);
    }

    private static ShellViewModel OpenVault(string vaultName)
    {
        var shell = new ShellViewModel();

        shell.OpenVault(Path.Combine(FixtureRoot, "vaults", vaultName));

        return shell;
    }

    private static VaultDocument Document(ShellViewModel shell, string relativePath) =>
        shell.Vault!.Index.ByRelativePath(relativePath).Single();

    private static string FixtureRoot { get; } = FindFixtures();

    private static string FindFixtures()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"tests/fixtures was not found above {AppContext.BaseDirectory}.");
    }
}
