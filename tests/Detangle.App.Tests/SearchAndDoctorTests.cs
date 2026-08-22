using Detangle.Core.Diagnostics;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the phase 5 panels as the shell drives them: live search, the Link Doctor,
/// and the "fix all safe" rewrite that writes to disk.
/// </summary>
public class SearchAndDoctorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-shell-" + Guid.NewGuid().ToString("N")[..8]);

    public SearchAndDoctorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));

        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "---\ntitle: Index\ntype: hub\n---\n\n# Index\n\nSee [[My Target]] and [[nowhere at all]].\n");

        File.WriteAllText(
            Path.Combine(_root, "notes", "my-target.md"),
            "---\ntitle: My Target\ntype: concept\ntags: [alpha/beta]\n---\n\n# My Target\n\nThe quarry.\n");
    }

    [Fact]
    public void SearchFindsTextAsTheQueryIsTyped()
    {
        ShellViewModel shell = Open();

        shell.SearchQuery = "quar";

        Assert.NotEmpty(shell.SearchResults);
        Assert.Contains(shell.SearchResults, h => h.Document.RelativePath == "notes/my-target.md");
        Assert.Equal("1 result", shell.SearchSummary);
    }

    [Fact]
    public void SearchNarrowsByField()
    {
        ShellViewModel shell = Open();

        shell.SearchQuery = "type:concept";

        Assert.All(shell.SearchResults, h => Assert.Equal("concept", h.Document.Frontmatter.Type));

        shell.SearchQuery = "tag:alpha";

        Assert.NotEmpty(shell.SearchResults);
    }

    [Fact]
    public void ClearingTheQueryClearsTheResults()
    {
        ShellViewModel shell = Open();

        shell.SearchQuery = "quarry";
        Assert.NotEmpty(shell.SearchResults);

        shell.SearchQuery = "  ";

        Assert.Empty(shell.SearchResults);
        Assert.Empty(shell.SearchSummary);
    }

    [Fact]
    public void TheDoctorReportsBrokenAndNonCanonicalLinks()
    {
        ShellViewModel shell = Open();

        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.NonCanonicalLink);
    }

    [Fact]
    public void FindingsAreOrderedBySeverity()
    {
        ShellViewModel shell = Open();

        List<FindingSeverity> severities = [.. shell.Findings.Select(f => f.Severity)];

        Assert.Equal(severities.OrderBy(s => s), severities);
    }

    [Fact]
    public void FixAllSafeRewritesLinksOnDiskAndClearsTheFindings()
    {
        ShellViewModel shell = Open();

        int written = shell.FixAllSafe();

        Assert.Equal(1, written);

        string rewritten = File.ReadAllText(Path.Combine(_root, "index.md"));

        Assert.Contains("[[notes/my-target]]", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain(shell.Findings, f => f.Kind == FindingKind.NonCanonicalLink);

        // The broken link has no single right answer, so it survives the safe pass.
        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
    }

    [Fact]
    public void FixAllSafeRewritesEveryLinkOnALineThatHasSeveral()
    {
        // The canonical target here is far shorter than the alias it replaces, so fixing
        // the left-hand link first pulls the right-hand one back from the column it was
        // recorded at, and scanning forward from a stale column walks straight past it.
        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "# Index\n\nSee [[The Quarry Page]] and also [[The Quarry Page]] again.\n");

        File.WriteAllText(
            Path.Combine(_root, "t.md"),
            "---\ntitle: T\naliases: [The Quarry Page]\n---\n\n# T\n");

        ShellViewModel shell = Open();

        Assert.Equal(1, shell.FixAllSafe());

        string rewritten = File.ReadAllText(Path.Combine(_root, "index.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal("# Index\n\nSee [[t]] and also [[t]] again.\n", rewritten);
    }

    [Fact]
    public void ABrokenFragmentIsReportedAndNamesTheHeadingItProbablyMeant()
    {
        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "# Index\n\nSee [[My Target#The Quary]].\n");

        File.WriteAllText(
            Path.Combine(_root, "notes", "my-target.md"),
            "# My Target\n\n## The Quarry\n\nHere.\n");

        ShellViewModel shell = Open();

        Finding finding = Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenAnchor);

        // The guess is not made until the reader opens the finding.
        Assert.Null(finding.SuggestedAnchor);

        shell.Suggest(finding);

        Assert.Equal(
            "The Quarry",
            Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenAnchor).SuggestedAnchor);

        // The triage tree binds to the groups, not to the flat list, so a suggestion that
        // only lands in one of them never reaches the panel.
        Assert.Equal(
            "The Quarry",
            Assert.Single(
                shell.FindingGroups.Single(g => g.Kind == FindingKind.BrokenAnchor).Findings)
                .SuggestedAnchor);
    }

    [Fact]
    public void FixAllSafeLeavesTheVaultAloneWhenThereIsNothingSafeToDo()
    {
        ShellViewModel shell = Open();

        shell.FixAllSafe();
        string after = File.ReadAllText(Path.Combine(_root, "index.md"));

        Assert.Equal(0, shell.FixAllSafe());
        Assert.Equal(after, File.ReadAllText(Path.Combine(_root, "index.md")));
    }

    [Fact]
    public void TheSidecarIndexLivesBesideTheVault()
    {
        Open();

        Assert.True(File.Exists(Path.Combine(_root, ".detangle", "cache.db")));
    }

    [Fact]
    public void EditsOnDiskAreReflectedAfterAReconcile()
    {
        ShellViewModel shell = Open();

        File.WriteAllText(
            Path.Combine(_root, "notes", "my-target.md"),
            "---\ntitle: My Target\ntype: concept\n---\n\n# My Target\n\nA completely new word: zeppelin.\n");

        // The watcher would do this on its own timer; calling it directly keeps the test
        // deterministic rather than sleeping and hoping.
        shell.Reconcile();
        shell.SearchQuery = "zeppelin";

        Assert.NotEmpty(shell.SearchResults);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private ShellViewModel Open()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(_root);

        return shell;
    }
}
