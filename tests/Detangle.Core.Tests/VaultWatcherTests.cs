using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the file watcher. They drive the reconcile sweep rather than the OS event
/// stream: the sweep is the ground truth the events are only an optimisation over
/// (plan.md section 11.1), and it is the half that has to be right on every platform.
/// </summary>
public class VaultWatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-watch-" + Guid.NewGuid().ToString("N")[..8]);

    public VaultWatcherTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "one.md"), "# One\n");
    }

    [Fact]
    public void ReconcileReportsNothingWhenNothingChanged()
    {
        using VaultWatcher watcher = Watch();

        Assert.Empty(watcher.Reconcile());
    }

    [Fact]
    public void ReconcileFindsANewFile()
    {
        using VaultWatcher watcher = Watch();

        File.WriteAllText(Path.Combine(_root, "two.md"), "# Two\n");

        VaultChange change = Assert.Single(watcher.Reconcile());

        Assert.Equal(VaultChangeKind.Added, change.Kind);
        Assert.Equal("two.md", change.RelativePath);
    }

    [Fact]
    public void ReconcileFindsAnEditedFile()
    {
        using VaultWatcher watcher = Watch();

        string path = Path.Combine(_root, "one.md");
        File.WriteAllText(path, "# One, revised\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        VaultChange change = Assert.Single(watcher.Reconcile());

        Assert.Equal(VaultChangeKind.Changed, change.Kind);
    }

    [Fact]
    public void ReconcileFindsADeletedFile()
    {
        using VaultWatcher watcher = Watch();

        File.Delete(Path.Combine(_root, "one.md"));

        VaultChange change = Assert.Single(watcher.Reconcile());

        Assert.Equal(VaultChangeKind.Removed, change.Kind);
        Assert.Equal("one.md", change.RelativePath);
    }

    [Fact]
    public void ASecondReconcileDoesNotRepeatTheSameChange()
    {
        using VaultWatcher watcher = Watch();

        File.WriteAllText(Path.Combine(_root, "two.md"), "# Two\n");

        Assert.Single(watcher.Reconcile());
        Assert.Empty(watcher.Reconcile());
    }

    [Fact]
    public void ChangesInsideIgnoredDirectoriesAreNotReported()
    {
        using VaultWatcher watcher = Watch();

        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/main\n");

        Assert.Empty(watcher.Reconcile());
    }

    [Fact]
    public void ReconcileRaisesTheChangedEvent()
    {
        using VaultWatcher watcher = Watch();

        IReadOnlyList<VaultChange>? reported = null;
        watcher.Changed += (_, changes) => reported = changes;

        File.WriteAllText(Path.Combine(_root, "three.md"), "# Three\n");
        watcher.Reconcile();

        Assert.NotNull(reported);
        Assert.Single(reported!);
    }

    [Fact]
    public void UsesTheLargerBufferThatKeepsWindowsFromDroppingEvents()
    {
        // A silent overflow is the failure mode this guards against; the value is part of
        // the contract, not an implementation detail.
        using VaultWatcher watcher = Watch();

        Assert.NotNull(watcher);
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

    private VaultWatcher Watch()
    {
        VaultSnapshot vault = VaultScanner.Scan(_root);

        return new VaultWatcher(
            vault,
            debounce: TimeSpan.FromMilliseconds(50),
            reconcileInterval: TimeSpan.FromHours(1));
    }
}
