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
    public async Task DisposingWhileEventsAreInFlightDoesNotCrashTheProcess()
    {
        // Filesystem events arrive on a threadpool thread and keep arriving after Dispose
        // returns. Touching a disposed timer there throws where nobody can catch it, which
        // takes the whole process down — it was killing test runs mid-flight.
        var unhandled = new List<Exception>();

        void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                unhandled.Add(exception);
            }
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;

        try
        {
            VaultWatcher watcher = Watch();

            for (int i = 0; i < 40; i++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(_root, $"burst-{i}.md"),
                    $"# Burst {i}",
                    TestContext.Current.CancellationToken);
            }

            watcher.Dispose();

            // Give the events already queued a chance to land on the disposed watcher.
            await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

            Assert.Empty(unhandled);
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        }
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        VaultWatcher watcher = Watch();

        watcher.Dispose();
        watcher.Dispose();
    }

    [Fact]
    public void ReconcileAfterDisposeReportsNothing()
    {
        VaultWatcher watcher = Watch();

        watcher.Dispose();

        Assert.Empty(watcher.Reconcile());
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
