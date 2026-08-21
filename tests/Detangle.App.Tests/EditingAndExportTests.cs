using Detangle.Core.Diagnostics;
using Detangle.Core.Editing;
using Detangle.Core.Vault;
using Detangle.Rendering.Export;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the shell's editing and export half (plan.md sections 6.5 and 6.6). These
/// run against a copy of a fixture vault, because every one of them writes to it.
/// </summary>
public class EditingAndExportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-shell-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EditingOpensTheActivePageAndClosesAgain()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");

        Assert.False(shell.IsEditing);

        shell.ToggleEditCommand.Execute(null);

        Assert.True(shell.IsEditing);
        Assert.NotEmpty(shell.EditorText);
        Assert.False(shell.HasUnsavedChanges);

        shell.ToggleEditCommand.Execute(null);

        Assert.False(shell.IsEditing);
        Assert.Empty(shell.EditorText);
    }

    [Fact]
    public void TypingMarksTheDocumentUnsavedAndSavingClearsIt()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        shell.ToggleEditCommand.Execute(null);

        string path = shell.ActiveTab!.Document.AbsolutePath;

        shell.EditorText += "\n\nA sentence the editor added.\n";

        Assert.True(shell.HasUnsavedChanges);
        Assert.Equal(SaveOutcome.Saved, shell.Save());
        Assert.False(shell.HasUnsavedChanges);
        Assert.Contains("A sentence the editor added.", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SavingOverAnExternalChangeIsRefusedUntilItIsForced()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        shell.ToggleEditCommand.Execute(null);

        string path = shell.ActiveTab!.Document.AbsolutePath;

        File.WriteAllText(path, "# Written by something else\n");
        shell.EditorText = "# Written by the editor\n";

        Assert.True(shell.HasExternalChange);
        Assert.Equal(SaveOutcome.Conflict, shell.Save());
        Assert.Equal("# Written by something else\n", File.ReadAllText(path));

        Assert.Equal(SaveOutcome.Saved, shell.Save(overwriteExternalChanges: true));
        Assert.Equal("# Written by the editor\n", File.ReadAllText(path));
    }

    [Fact]
    public void ReloadingThrowsAwayTheEditorsCopy()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        shell.ToggleEditCommand.Execute(null);

        File.WriteAllText(shell.ActiveTab!.Document.AbsolutePath, "# From elsewhere\n");
        shell.EditorText = "# Mine\n";

        shell.ReloadDocumentCommand.Execute(null);

        Assert.Equal("# From elsewhere\n", shell.EditorText);
        Assert.False(shell.HasUnsavedChanges);
    }

    [Fact]
    public void SwitchingTabsClosesTheEditor()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        shell.ToggleEditCommand.Execute(null);

        VaultDocument other = shell.Vault!.Documents.First(
            d => d.IsMarkdown && d.RelativePath != shell.ActiveTab!.Document.RelativePath);

        shell.Open(other);

        Assert.False(shell.IsEditing);
    }

    [Fact]
    public void SavingRebuildsTheVaultSoNewLinksResolve()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        shell.ToggleEditCommand.Execute(null);

        int before = shell.Graph!.Resolutions.Count;

        shell.EditorText += "\n\nAn added link to [[wiki/index]].\n";
        shell.Save();

        Assert.True(
            shell.Graph!.Resolutions.Count > before,
            "the link that was just typed should be in the graph");
    }

    [Fact]
    public void CreatingTheMissingNoteResolvesTheBrokenLink()
    {
        ShellViewModel shell = OpenCopy("torture");

        Finding broken = shell.Findings.First(f => f.Kind == FindingKind.BrokenLink);
        int before = shell.Findings.Count(f => f.Kind == FindingKind.BrokenLink);

        VaultDocument? created = shell.CreateMissingNote(broken);

        Assert.NotNull(created);
        Assert.True(File.Exists(created!.AbsolutePath));
        Assert.Equal(created.RelativePath, shell.ActiveTab!.Document.RelativePath);
        Assert.True(
            shell.Findings.Count(f => f.Kind == FindingKind.BrokenLink) < before,
            "creating the page should have fixed at least the link that asked for it");
    }

    [Fact]
    public void NormalizingRewritesTheVaultAndLosesNoLinks()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");

        var before = shell.Graph!.Resolutions
            .Select(r => r.Target?.RelativePath)
            .ToList();

        int changed = shell.NormalizeVault();

        Assert.True(changed > 0, "the fixture vault links by title, so something should have been rewritten");

        // The whole point: rewriting must not change where a single link goes.
        Assert.Equal(before, shell.Graph!.Resolutions.Select(r => r.Target?.RelativePath));
        Assert.Contains("Rewrote", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportingASiteWritesItAndReportsWhatItWrote()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        string output = Path.Combine(_root, "..", Path.GetFileName(_root) + "-site");

        try
        {
            ExportReport? report = shell.ExportSite(output);

            Assert.NotNull(report);
            Assert.True(report!.Pages > 0);
            Assert.True(File.Exists(Path.Combine(output, "detangle.css")));
            Assert.Contains("Exported", shell.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void ExportingOnePageAsHtmlWritesOnlyThatPage()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        string output = Path.Combine(_root, "page.html");

        ExportReport? report = shell.ExportSingleFile(output, currentPageOnly: true);

        Assert.Equal(1, report?.Pages);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void ExportingAPdfWritesAPdf()
    {
        ShellViewModel shell = OpenCopy("llm-wiki");
        string output = Path.Combine(_root, "vault.pdf");

        ExportReport? report = shell.ExportPdf(output);

        Assert.NotNull(report);
        Assert.True(new FileInfo(output).Length > 1000);
    }

    [Fact]
    public void ExportingWithoutAVaultDoesNothing()
    {
        var shell = new ShellViewModel();

        Assert.Null(shell.ExportSite(_root));
        Assert.Null(shell.ExportPdf(Path.Combine(_root, "x.pdf")));
        Assert.Equal(0, shell.NormalizeVault());
    }

    /// <summary>
    /// Copies a fixture vault somewhere writable and opens it. The fixtures are checked
    /// in, and a test that edits them would break every other test in the repository.
    /// </summary>
    private ShellViewModel OpenCopy(string vaultName)
    {
        string source = Path.Combine(FixtureRoot, "vaults", vaultName);

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(source, file);

            // The search index is a sidecar Detangle writes into the vault it opens, so a
            // fixture that another test has opened has one — including its SQLite journal,
            // which may be gone again by the time this copy reaches it. None of it belongs
            // in a copy that is about to be scanned from scratch.
            if (relativePath.StartsWith(".detangle", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(_root, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        var shell = new ShellViewModel();

        shell.OpenVault(_root);

        Assert.True(shell.HasVault);

        return shell;
    }

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

        throw new DirectoryNotFoundException("tests/fixtures was not found above the test binaries.");
    }
}
