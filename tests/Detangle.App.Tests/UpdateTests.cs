using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for in-app update (plan.md section 8, phase 8). The updater itself belongs to
/// the desktop head — it needs an installed application to talk to — so what is tested
/// here is the decision the shell makes around it.
/// </summary>
public class UpdateTests
{
    [Fact]
    public async Task ACopyThatWasNotInstalledSaysSoRatherThanCheckingAsync()
    {
        var shell = new ShellViewModel { UpdateService = new FakeUpdateService { IsInstalled = false } };

        await shell.CheckForUpdatesAsync();

        Assert.False(shell.HasUpdate);
        Assert.Contains("not installed by the updater", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoServiceAtAllIsNotACrashAsync()
    {
        var shell = new ShellViewModel();

        await shell.CheckForUpdatesAsync();
        await shell.InstallUpdateAsync();

        Assert.False(shell.HasUpdate);
    }

    [Fact]
    public async Task BeingUpToDateIsReportedAsync()
    {
        var shell = new ShellViewModel { UpdateService = new FakeUpdateService() };

        await shell.CheckForUpdatesAsync();

        Assert.False(shell.HasUpdate);
        Assert.Contains("up to date", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAvailableUpdateIsOfferedRatherThanInstalledAsync()
    {
        var service = new FakeUpdateService { Available = new AvailableUpdate("1.4.0", IsDelta: true) };
        var shell = new ShellViewModel { UpdateService = service };

        await shell.CheckForUpdatesAsync();

        // A reading app that restarts itself while somebody is reading has misunderstood
        // what it is for.
        Assert.True(shell.HasUpdate);
        Assert.Equal("Update to 1.4.0 (delta)", shell.UpdateSummary);
        Assert.False(service.Applied);
    }

    [Fact]
    public async Task InstallingAppliesThePendingUpdateAsync()
    {
        var service = new FakeUpdateService { Available = new AvailableUpdate("1.4.0", IsDelta: false) };
        var shell = new ShellViewModel { UpdateService = service };

        await shell.CheckForUpdatesAsync();
        await shell.InstallUpdateAsync();

        Assert.True(service.Applied);
    }

    [Fact]
    public async Task InstallingWithNothingPendingDoesNothingAsync()
    {
        var service = new FakeUpdateService();
        var shell = new ShellViewModel { UpdateService = service };

        await shell.InstallUpdateAsync();

        Assert.False(service.Applied);
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsInstalled { get; init; } = true;

        public AvailableUpdate? Available { get; init; }

        public bool Applied { get; private set; }

        public Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);

        public Task ApplyAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
        {
            Applied = true;

            return Task.CompletedTask;
        }
    }
}
