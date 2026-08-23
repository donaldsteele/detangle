using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the recent-vault list, which lives in this reader's own configuration
/// directory rather than in any vault — the vault you no longer have in front of you is
/// exactly the one the list has to be able to name.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "detangle-settings-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    [Fact]
    public void AVaultOpenedIsRememberedWithItsFlavor()
    {
        AppSettings settings = AppSettings.Open(_path);

        Assert.True(settings.Remember(Vault("/vaults/wiki", VaultFlavor.Obsidian), When(1)));

        RecentVault recent = Assert.Single(AppSettings.Open(_path).Recent);

        Assert.Equal("/vaults/wiki", recent.Path);
        Assert.Equal("Obsidian", recent.Flavor);
        Assert.Equal("wiki", recent.Name);
    }

    [Fact]
    public void ReopeningAVaultMovesItRatherThanListingItTwice()
    {
        AppSettings settings = AppSettings.Open(_path);

        settings.Remember(Vault("/vaults/first"), When(1));
        settings.Remember(Vault("/vaults/second"), When(2));
        settings.Remember(Vault("/vaults/first"), When(3));

        Assert.Equal(["/vaults/first", "/vaults/second"], settings.Recent.Select(r => r.Path));
    }

    [Fact]
    public void ATrailingSeparatorIsTheSameFolder()
    {
        AppSettings settings = AppSettings.Open(_path);

        settings.Remember(Vault("/vaults/wiki"), When(1));
        settings.Remember(Vault("/vaults/wiki/"), When(2));

        Assert.Single(settings.Recent);
    }

    [Fact]
    public void TheListStopsAtTenSoItStaysAList()
    {
        AppSettings settings = AppSettings.Open(_path);

        for (int i = 0; i < AppSettings.RecentLimit + 5; i++)
        {
            settings.Remember(Vault($"/vaults/{i}"), When(i));
        }

        Assert.Equal(AppSettings.RecentLimit, settings.Recent.Count);
        Assert.Equal("/vaults/14", settings.Recent[0].Path);
    }

    [Fact]
    public void AFolderThatIsGoneIsMarkedRatherThanDropped()
    {
        AppSettings settings = AppSettings.Open(_path);

        settings.Remember(Vault("/vaults/deleted-since"), When(1));

        RecentVault recent = Assert.Single(settings.Recent);

        // Silently losing the entry would leave a reader wondering whether they had
        // imagined opening it.
        Assert.False(recent.Exists);
    }

    [Fact]
    public void SettingsWithNowhereToWriteRememberNothingAndSaySo()
    {
        // The browser head: a list of folders on a machine the tab cannot reach would be a
        // list of dead links.
        Assert.False(AppSettings.None.Remember(Vault("/vaults/wiki"), When(1)));
        Assert.Empty(AppSettings.None.Recent);
    }

    [Fact]
    public void AnUnreadableFileCostsTheListRatherThanTheApplication()
    {
        File.WriteAllText(_path, "this is not json at all");

        Assert.Empty(AppSettings.Open(_path).Recent);
    }

    private static VaultSnapshot Vault(string root, VaultFlavor flavor = VaultFlavor.Generic) =>
        new()
        {
            RootPath = root,
            Profile = VaultProfile.For(flavor),
            Index = VaultIndex.Build([]),
        };

    private static DateTimeOffset When(int step) =>
        new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero).AddMinutes(step);

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A temp file that outlives the run is not a failed test.
        }
    }
}
