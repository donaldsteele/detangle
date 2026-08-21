using Detangle.App;
using Velopack;
using Velopack.Sources;

namespace Detangle.Desktop;

/// <summary>
/// In-app update against the Velopack feed the release workflow publishes.
/// <para>
/// The feed is a GitHub release, which means the updater needs no server of its own and
/// no account: it reads the same releases a human would download from. Deltas come for
/// free — Velopack ships one when the changed files are smaller than the whole app,
/// which for a 40 MB self-contained Avalonia build is nearly always.
/// </para>
/// </summary>
internal sealed class VelopackUpdateService : IUpdateService
{
    private const string Repository = "https://github.com/donaldsteele/detangle";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public VelopackUpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            // A build that was not installed by Velopack has no package directory to look
            // at. That is the normal case in development, and it is not an error.
            _manager = null;
        }
    }

    /// <inheritdoc />
    public bool IsInstalled => _manager is { IsInstalled: true };

    /// <inheritdoc />
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is not { IsInstalled: true } manager)
        {
            return null;
        }

        try
        {
            _pending = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // An offline reader is the expected state for a local-first app, not a
            // failure worth interrupting them over.
            return null;
        }

        return _pending is null
            ? null
            : new AvailableUpdate(
                _pending.TargetFullRelease.Version.ToString(),
                _pending.DeltasToTarget.Length > 0);
    }

    /// <inheritdoc />
    public async Task ApplyAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (_manager is not { IsInstalled: true } manager || _pending is not { } pending)
        {
            return;
        }

        await manager.DownloadUpdatesAsync(pending).WaitAsync(cancellationToken).ConfigureAwait(false);

        // Nothing after this runs: the process is replaced by the new version.
        manager.ApplyUpdatesAndRestart(pending);
    }
}
