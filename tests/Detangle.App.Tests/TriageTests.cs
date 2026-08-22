using Detangle.Core.Diagnostics;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the triage deck (plan.md section 15.3): grouping, the before/after preview,
/// applying one finding, and an ignore that survives the corpus being regenerated.
/// </summary>
public class TriageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-triage-" + Guid.NewGuid().ToString("N")[..8]);

    public TriageTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));

        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "# Index\n\nSee [[My Target]] and [[nowhere at all]].\n");

        File.WriteAllText(
            Path.Combine(_root, "notes", "my-target.md"),
            "# My Target\n\nThe quarry.\n");
    }

    [Fact]
    public void FindingsAreGroupedByKindWorstFirst()
    {
        ShellViewModel shell = Open();

        Assert.NotEmpty(shell.FindingGroups);
        Assert.Equal(FindingKind.BrokenLink, shell.FindingGroups[0].Kind);

        // Every finding is in exactly one group, and the counts add up.
        Assert.Equal(shell.Findings.Count, shell.FindingGroups.Sum(g => g.Findings.Count));

        FindingGroup broken = shell.FindingGroups.First(g => g.Kind == FindingKind.BrokenLink);

        Assert.Equal("Broken link · 1", broken.Title);
    }

    [Fact]
    public void ThePreviewIsTheRewriteRatherThanAGuessAtIt()
    {
        ShellViewModel shell = Open();

        Finding finding = Assert.Single(shell.Findings, f => f.Kind == FindingKind.NonCanonicalLink);

        (string Before, string After)? preview = shell.PreviewFix(finding);

        Assert.NotNull(preview);
        Assert.Equal("See [[My Target]] and [[nowhere at all]].", preview.Value.Before);
        Assert.Equal("See [[notes/my-target]] and [[nowhere at all]].", preview.Value.After);

        // Previewing is not applying.
        Assert.Contains("[[My Target]]", File.ReadAllText(Path.Combine(_root, "index.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void AFindingWithNothingToApplyHasNoPreview()
    {
        ShellViewModel shell = Open();

        Assert.Null(shell.PreviewFix(Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenLink)));
        Assert.Null(shell.PreviewFix(Assert.Single(shell.Findings, f => f.Kind == FindingKind.OrphanPage)));
    }

    [Fact]
    public void ApplyingOneFindingWritesThatLinkAndLeavesTheRest()
    {
        ShellViewModel shell = Open();

        Assert.True(shell.ApplyFix(Assert.Single(shell.Findings, f => f.Kind == FindingKind.NonCanonicalLink)));

        string content = File.ReadAllText(Path.Combine(_root, "index.md"));

        Assert.Contains("[[notes/my-target]]", content, StringComparison.Ordinal);

        // The broken link on the same line had no single right answer and is untouched.
        Assert.Contains("[[nowhere at all]]", content, StringComparison.Ordinal);
        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
    }

    [Fact]
    public void AFindingWithNoRewriteIsRefusedRatherThanGuessedAt()
    {
        ShellViewModel shell = Open();

        Assert.False(shell.ApplyFix(Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenLink)));
    }

    [Fact]
    public void IgnoringAFindingHidesItWithoutTouchingTheFile()
    {
        ShellViewModel shell = Open();

        string before = File.ReadAllText(Path.Combine(_root, "index.md"));
        Finding finding = Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenLink);

        shell.Ignore(finding);

        Assert.DoesNotContain(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
        Assert.DoesNotContain(shell.FindingGroups, g => g.Kind == FindingKind.BrokenLink);
        Assert.Equal(1, shell.IgnoredCount);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "index.md")));

        // And it stays ignored for the next reader of this vault.
        Assert.DoesNotContain(Open().Findings, f => f.Kind == FindingKind.BrokenLink);
    }

    [Fact]
    public void AnIgnoreSurvivesTheCorpusBeingRegenerated()
    {
        ShellViewModel shell = Open();

        shell.Ignore(Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenLink));

        // The generator runs again and every line moves. An ignore keyed on the line
        // number would come back here, which is the moment a reader least wants to
        // re-triage a list they already went through.
        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "---\ntitle: Index\n---\n\n# Index\n\nAn added paragraph.\n\nSee [[My Target]] and [[nowhere at all]].\n");

        Assert.DoesNotContain(Open().Findings, f => f.Kind == FindingKind.BrokenLink);
    }

    [Fact]
    public void ShowingIgnoredBringsThemAllBack()
    {
        ShellViewModel shell = Open();

        shell.Ignore(Assert.Single(shell.Findings, f => f.Kind == FindingKind.BrokenLink));
        shell.ShowIgnored();

        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
        Assert.Equal(0, shell.IgnoredCount);
    }

    private ShellViewModel Open()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(_root);

        return shell;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not a failed test.
        }
    }
}
