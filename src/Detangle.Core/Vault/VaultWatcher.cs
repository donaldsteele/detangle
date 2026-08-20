namespace Detangle.Core.Vault;

/// <summary>What happened to a file.</summary>
public enum VaultChangeKind
{
    /// <summary>A file appeared.</summary>
    Added,

    /// <summary>A file's contents changed.</summary>
    Changed,

    /// <summary>A file went away.</summary>
    Removed,
}

/// <summary>One coalesced change.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="RelativePath">The file, vault-relative with "/" separators.</param>
public sealed record VaultChange(VaultChangeKind Kind, string RelativePath);

/// <summary>
/// Watches a vault for edits made outside the app.
/// <para>
/// Three defences against the platform failures listed in plan.md section 11.1. Events
/// are debounced, because one save produces several. The buffer is enlarged, because
/// Windows silently drops events when it overflows. And a periodic reconcile sweep runs
/// regardless, because macOS coalesces and Linux runs out of inotify watches — so the
/// watcher is treated as an optimisation over the sweep rather than as the truth.
/// </para>
/// </summary>
public sealed class VaultWatcher : IDisposable
{
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;
    private readonly System.Timers.Timer _reconcile;
    private readonly Dictionary<string, VaultChangeKind> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly VaultScanOptions _options;

    /// <summary>Starts watching a vault.</summary>
    /// <param name="vault">The scanned vault.</param>
    /// <param name="options">The scan options, so the watcher ignores what the scan did.</param>
    /// <param name="debounce">How long to wait for a burst of events to settle.</param>
    /// <param name="reconcileInterval">How often to sweep for changes events may have missed.</param>
    public VaultWatcher(
        VaultSnapshot vault,
        VaultScanOptions? options = null,
        TimeSpan? debounce = null,
        TimeSpan? reconcileInterval = null)
    {
        _root = vault.RootPath;
        _options = options ?? VaultScanOptions.Default;

        foreach (VaultDocument document in vault.Documents)
        {
            _known[document.RelativePath] = document.LastModified.UtcDateTime;
        }

        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,

            // The Windows watcher drops events silently once its buffer overflows; 64 KB
            // is the documented ceiling and the difference between a reliable watch and a
            // vault that stops updating without saying so.
            InternalBufferSize = 64 * 1024,
        };

        _watcher.Created += (_, e) => Queue(VaultChangeKind.Added, e.FullPath);
        _watcher.Changed += (_, e) => Queue(VaultChangeKind.Changed, e.FullPath);
        _watcher.Deleted += (_, e) => Queue(VaultChangeKind.Removed, e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Queue(VaultChangeKind.Removed, e.OldFullPath);
            Queue(VaultChangeKind.Added, e.FullPath);
        };

        _watcher.Error += (_, _) => RestartWatcher();

        _debounce = new System.Timers.Timer((debounce ?? TimeSpan.FromMilliseconds(250)).TotalMilliseconds)
        {
            AutoReset = false,
        };

        _debounce.Elapsed += (_, _) => Flush();

        _reconcile = new System.Timers.Timer(
            (reconcileInterval ?? TimeSpan.FromSeconds(45)).TotalMilliseconds)
        {
            AutoReset = true,
        };

        _reconcile.Elapsed += (_, _) => Reconcile();

        _watcher.EnableRaisingEvents = true;
        _reconcile.Start();
    }

    /// <summary>Raised, off the UI thread, once a burst of changes has settled.</summary>
    public event EventHandler<IReadOnlyList<VaultChange>>? Changed;

    /// <summary>Raised when the watcher had to be recreated after an error.</summary>
    public event EventHandler<string>? WatcherRestarted;

    /// <summary>
    /// Compares the directory against what was last seen and reports the difference. This
    /// runs on a timer and can also be called directly — it is the ground truth the
    /// event stream is only an optimisation over.
    /// </summary>
    public IReadOnlyList<VaultChange> Reconcile()
    {
        var current = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(_root, file).Replace('\\', '/');

                if (IsIgnored(relative))
                {
                    continue;
                }

                current[relative] = File.GetLastWriteTimeUtc(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var changes = new List<VaultChange>();

        lock (_gate)
        {
            foreach ((string path, DateTime modified) in current)
            {
                if (!_known.TryGetValue(path, out DateTime previous))
                {
                    changes.Add(new VaultChange(VaultChangeKind.Added, path));
                }
                else if (previous != modified)
                {
                    changes.Add(new VaultChange(VaultChangeKind.Changed, path));
                }
            }

            foreach (string path in _known.Keys.Where(p => !current.ContainsKey(p)).ToList())
            {
                changes.Add(new VaultChange(VaultChangeKind.Removed, path));
            }

            _known.Clear();

            foreach ((string path, DateTime modified) in current)
            {
                _known[path] = modified;
            }
        }

        if (changes.Count > 0)
        {
            Changed?.Invoke(this, changes);
        }

        return changes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
        _reconcile.Dispose();
    }

    private void Queue(VaultChangeKind kind, string fullPath)
    {
        string relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');

        if (IsIgnored(relative))
        {
            return;
        }

        lock (_gate)
        {
            // A create followed by writes is still a create; a delete after anything is a
            // delete. Collapsing here is what turns one save into one event.
            _pending[relative] = _pending.TryGetValue(relative, out VaultChangeKind existing)
                && existing == VaultChangeKind.Added && kind == VaultChangeKind.Changed
                    ? VaultChangeKind.Added
                    : kind;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        List<VaultChange> changes;

        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            changes = [.. _pending.Select(entry => new VaultChange(entry.Value, entry.Key))];
            _pending.Clear();

            foreach (VaultChange change in changes)
            {
                string absolute = Path.Combine(_root, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                if (change.Kind == VaultChangeKind.Removed)
                {
                    _known.Remove(change.RelativePath);
                }
                else if (File.Exists(absolute))
                {
                    _known[change.RelativePath] = File.GetLastWriteTimeUtc(absolute);
                }
            }
        }

        Changed?.Invoke(this, changes);
    }

    private void RestartWatcher()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;

            WatcherRestarted?.Invoke(this, "The file watcher overflowed and was restarted; reconciling.");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            WatcherRestarted?.Invoke(
                this,
                "The file watcher could not be restarted. On Linux this is usually the inotify "
                    + "limit: sysctl -w fs.inotify.max_user_watches=524288");
        }

        Reconcile();
    }

    private bool IsIgnored(string relativePath)
    {
        foreach (string segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1])
        {
            if (_options.IgnoredDirectories.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
