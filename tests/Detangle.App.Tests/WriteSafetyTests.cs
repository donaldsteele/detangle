using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the confirm step in front of the two vault-wide writers.
/// <para>
/// Both used to rewrite every markdown file in the vault straight off a click — no
/// confirmation, no list of what would change, no undo — in a product whose stated promise
/// is that it is non-destructive by default. What these pin is that proposing a write
/// touches nothing at all.
/// </para>
/// </summary>
public class WriteSafetyTests : IDisposable
{
    private const string IndexContent = "# Index\n\nSee [[My Target]] and [[Second Page]].\n";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-write-" + Guid.NewGuid().ToString("N")[..8]);

    public WriteSafetyTests()
    {
        Directory.CreateDirectory(_root);

        // Two links that resolve by a late rung and have exactly one canonical form, so
        // "fix all safe" has real work to propose.
        File.WriteAllText(Path.Combine(_root, "index.md"), IndexContent);
        File.WriteAllText(Path.Combine(_root, "my-target.md"), "# My Target\n");
        File.WriteAllText(Path.Combine(_root, "second-page.md"), "# Second Page\n");
    }

    [Fact]
    public void ProposingAFixListsEveryFileAndWritesNone()
    {
        ShellViewModel shell = Open();

        shell.ProposeFixAllSafe();

        PendingWrite pending = Assert.IsType<PendingWrite>(shell.PendingWrite);

        Assert.Equal("Fix all safe", pending.Title);
        Assert.Equal("index.md", Assert.Single(pending.Files).Path);
        Assert.Equal(2, pending.ChangeCount);
        Assert.Contains("cannot be undone", pending.Summary, StringComparison.Ordinal);

        // The whole point: nothing on disk has moved.
        Assert.Equal(IndexContent, Read("index.md"));
    }

    [Fact]
    public void ConfirmingWritesAndClearsTheCard()
    {
        ShellViewModel shell = Open();

        shell.ProposeFixAllSafe();

        Assert.Equal(1, shell.ConfirmPendingWrite());
        Assert.Null(shell.PendingWrite);
        Assert.Contains("[[my-target]]", Read("index.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingLeavesTheVaultExactlyAsItWas()
    {
        ShellViewModel shell = Open();

        shell.ProposeFixAllSafe();
        shell.CancelPendingWrite();

        Assert.Null(shell.PendingWrite);
        Assert.Equal(IndexContent, Read("index.md"));
        Assert.Contains("No file was changed", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingWithNothingProposedDoesNothing()
    {
        ShellViewModel shell = Open();

        Assert.Equal(0, shell.ConfirmPendingWrite());
        Assert.Equal(IndexContent, Read("index.md"));
    }

    [Fact]
    public void ProposingNormalizationCountsTheLinksItWouldRewrite()
    {
        ShellViewModel shell = Open();

        shell.ProposeNormalizeVault();

        PendingWrite pending = Assert.IsType<PendingWrite>(shell.PendingWrite);

        Assert.Equal("Normalize links in place", pending.Title);
        Assert.NotEmpty(pending.Files);
        Assert.Equal(IndexContent, Read("index.md"));

        // And the dry run is the same pass as the write, so the count it showed is the
        // count the write produces.
        int changed = shell.ConfirmPendingWrite();

        Assert.Equal(pending.FileCount, changed);
    }

    [Fact]
    public void AVaultWithNothingToDoSaysSoRatherThanOfferingAnEmptyConfirm()
    {
        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[my-target]].\n");

        ShellViewModel shell = Open();

        shell.ProposeFixAllSafe();

        Assert.Null(shell.PendingWrite);
        Assert.Contains("Nothing here", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMessageIsKeptInFullEvenWhenTheBarTrimsIt()
    {
        ShellViewModel shell = Open();

        shell.Status = "a short one";
        shell.LogDetail("a very long exception, untrimmed, with a stack trace after it");

        Assert.Contains("a short one", shell.StatusLog);
        Assert.Contains(shell.StatusLog, m => m.Contains("stack trace", StringComparison.Ordinal));
        Assert.Contains("a short one", shell.StatusLogText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogStopsGrowingSoItIsNotALeak()
    {
        ShellViewModel shell = Open();

        for (int i = 0; i < ShellViewModel.StatusLogLimit + 20; i++)
        {
            shell.Status = $"message {i}";
        }

        Assert.Equal(ShellViewModel.StatusLogLimit, shell.StatusLog.Count);

        // Oldest first out, so what is kept is what just happened.
        Assert.Equal($"message {ShellViewModel.StatusLogLimit + 19}", shell.StatusLog[^1]);
    }

    [Fact]
    public void TheGraphHoverReadoutNoLongerWritesToTheStatusBar()
    {
        ShellViewModel shell = Open();

        shell.Status = "an export failed, at length";
        shell.GraphHover = "some-node · 3 in · 1 out";

        // Moving a mouse across the graph used to erase whatever the bar was saying,
        // including the one-line report of a failure that had just happened.
        Assert.Equal("an export failed, at length", shell.Status);
    }

    private ShellViewModel Open()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(_root);

        return shell;
    }

    private string Read(string relativePath) => File.ReadAllText(Path.Combine(_root, relativePath));

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
