using Detangle.Core.Diagnostics;
using Detangle.Core.History;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the regeneration diff (plan.md section 15.4) — the resolution ladder with a
/// time axis.
/// </summary>
public class RegenerationDiffTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-delta-" + Guid.NewGuid().ToString("N")[..8]);

    public RegenerationDiffTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));

        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[notes/target]].\n");
        File.WriteAllText(Path.Combine(_root, "notes", "target.md"), "# Target\n\nHere.\n");
    }

    [Fact]
    public void WithNoBaselineNothingHasChanged()
    {
        ShellViewModel shell = Open();

        Assert.True(shell.Delta.IsEmpty);

        // A whole vault reported as new on first open would be noise, not news.
        Assert.Null(shell.ChangeSummary);
    }

    [Fact]
    public void ALinkThatStillWorksButWorksWorseIsReported()
    {
        ShellViewModel shell = Open();
        shell.MarkBaseline();

        // The generator moves the page. The link still resolves - that is the product -
        // but it now takes a later rung of the ladder to do it, and no other tool in the
        // category can represent a link that works and yet is worse than it was.
        File.Move(Path.Combine(_root, "notes", "target.md"), Path.Combine(_root, "target.md"));
        shell.Reconcile();

        LinkChange change = Assert.Single(shell.Delta.Links);

        Assert.Equal(LinkChangeKind.Degraded, change.Kind);
        Assert.Equal("notes/target", change.RawTarget);
        Assert.True(change.After.Rule > change.Before.Rule);
        Assert.Contains("1 link now needs a later rule", shell.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkThatStoppedResolvingIsReportedAsBroken()
    {
        ShellViewModel shell = Open();
        shell.MarkBaseline();

        File.Delete(Path.Combine(_root, "notes", "target.md"));
        shell.Reconcile();

        Assert.Equal(LinkChangeKind.Broke, Assert.Single(shell.Delta.Links).Kind);
        Assert.Contains("1 link broke", shell.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void RewritingAFileIdenticallyIsNotAChange()
    {
        ShellViewModel shell = Open();
        shell.MarkBaseline();

        // A generator stamps a fresh modification time on every file it emits, including
        // the ones it emitted byte for byte the same. Hashing the normalized text is what
        // keeps that from reporting the whole vault as rewritten.
        string path = Path.Combine(_root, "notes", "target.md");
        string content = File.ReadAllText(path);

        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        shell.Reconcile();

        Assert.True(shell.Delta.IsEmpty);
    }

    [Fact]
    public void LineEndingsAloneAreNotAChange()
    {
        ShellViewModel shell = Open();
        shell.MarkBaseline();

        // Checking a Linux-written repository out on Windows rewrites every line ending.
        // Reporting that as the whole vault rewritten would make the feature useless
        // exactly where this product's corpora live.
        string path = Path.Combine(_root, "notes", "target.md");

        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));
        shell.Reconcile();

        Assert.Empty(shell.Delta.Rewritten);
    }

    [Fact]
    public void AMovedPageIsARenameRatherThanALossAndAGain()
    {
        File.WriteAllText(
            Path.Combine(_root, "notes", "target.md"),
            "---\nid: t-1\ntitle: Target\n---\n\n# Target\n");

        ShellViewModel shell = Open();
        shell.MarkBaseline();

        File.Move(Path.Combine(_root, "notes", "target.md"), Path.Combine(_root, "moved.md"));
        shell.Reconcile();

        KeyValuePair<string, string> rename = Assert.Single(shell.Delta.Renamed);

        Assert.Equal("notes/target.md", rename.Key);
        Assert.Equal("moved.md", rename.Value);
        Assert.Empty(shell.Delta.Added);
        Assert.Empty(shell.Delta.Removed);
    }

    [Fact]
    public void TheBaselineOutlivesTheSession()
    {
        Open().MarkBaseline();

        File.Delete(Path.Combine(_root, "notes", "target.md"));

        // A second reader of the same vault is told what the first one's baseline says.
        Assert.Equal(LinkChangeKind.Broke, Assert.Single(Open().Delta.Links).Kind);
    }

    [Fact]
    public void TheSinceFilterNarrowsTheTriageListToWhatMoved()
    {
        File.WriteAllText(Path.Combine(_root, "untouched.md"), "# Untouched\n\nSee [[nowhere at all]].\n");

        ShellViewModel shell = Open();
        shell.MarkBaseline();

        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[also nowhere]].\n");
        shell.Reconcile();

        Assert.Contains(shell.Findings, f => f.Document.RelativePath == "untouched.md");

        shell.ShowOnlyChanged = true;

        Assert.All(shell.Findings, f => Assert.Equal("index.md", f.Document.RelativePath));
        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.BrokenLink);
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
