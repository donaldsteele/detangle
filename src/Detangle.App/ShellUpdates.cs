using CommunityToolkit.Mvvm.Input;

namespace Detangle.App;

/// <summary>
/// The shell's update half (plan.md section 8, phase 8).
/// <para>
/// Updating is offered, never done. A local-first reading app that restarts itself while
/// somebody is reading has misunderstood what it is for, so the check reports and the
/// reader decides.
/// </para>
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>How updates are checked; null in a build that cannot update itself.</summary>
    public IUpdateService? UpdateService { get; init; }

    /// <summary>The update that is waiting, when one is.</summary>
    public AvailableUpdate? PendingUpdate
    {
        get => _pendingUpdate;
        private set
        {
            if (SetProperty(ref _pendingUpdate, value))
            {
                OnPropertyChanged(nameof(HasUpdate));
                OnPropertyChanged(nameof(UpdateSummary));
            }
        }
    }

    private AvailableUpdate? _pendingUpdate;

    /// <summary>True when there is a newer version to install.</summary>
    public bool HasUpdate => PendingUpdate is not null;

    /// <summary>What the update button says.</summary>
    public string UpdateSummary => PendingUpdate is { } update
        ? $"Update to {update.Version}{(update.IsDelta ? " (delta)" : string.Empty)}"
        : string.Empty;

    /// <summary>Asks the release feed whether there is a newer version.</summary>
    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (UpdateService is not { IsInstalled: true } service)
        {
            Status = "This copy was not installed by the updater, so it updates itself the way it was installed.";
            return;
        }

        Status = "Checking for updates…";

        PendingUpdate = await service.CheckAsync().ConfigureAwait(true);

        Status = PendingUpdate is null ? "Detangle is up to date." : UpdateSummary;
    }

    /// <summary>Downloads and installs the pending update; the app restarts into it.</summary>
    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (UpdateService is not { } service || PendingUpdate is not { } update)
        {
            return;
        }

        Status = $"Downloading {update.Version}…";

        await service.ApplyAsync(update).ConfigureAwait(true);
    }
}
