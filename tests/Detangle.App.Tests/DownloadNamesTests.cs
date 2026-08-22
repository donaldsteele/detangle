using System.Text.RegularExpressions;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Guards that the download section offers files the release actually produces.
/// <para>
/// It offered <c>Detangle-osx-arm64.dmg</c> and <c>Detangle-win-x64.exe</c> for a release
/// and a half. Neither has ever existed: Velopack packs a <c>.pkg</c> on macOS and names
/// the Windows installer <c>-Setup.exe</c>. Nothing caught it because nothing compared the
/// page against the workflow that fills the release page, which is what this does.
/// </para>
/// </summary>
public partial class DownloadNamesTests
{
    private static readonly string[] RuntimeIdentifiers =
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    [Fact]
    public void EveryOfferedFileIsOneTheReleaseWorkflowProduces()
    {
        IReadOnlyList<string> offered = Offered();

        Assert.NotEmpty(offered);

        foreach (string file in offered)
        {
            Assert.True(
                Produced().Contains(file),
                $"the download section offers {file}, which .github/workflows/release.yml never builds");
        }
    }

    [Fact]
    public void TheObviousDownloadForEachPlatformIsOffered()
    {
        IReadOnlyList<string> offered = Offered();

        // The one a visitor on that platform should take without thinking about it. If a
        // rename ever drops one of these, the page still lists files - just not the ones
        // that matter - which is the failure this catches.
        Assert.Contains("Detangle-osx-arm64-Setup.pkg", offered);
        Assert.Contains("Detangle-win-x64-Setup.exe", offered);
        Assert.Contains("Detangle-linux-x64.AppImage", offered);
    }

    [Fact]
    public void TheSectionStillWorksWithoutJavaScript()
    {
        string html = Read("index.html");

        // All three platforms are in the markup, always. site.js highlights one and adds a
        // button; it never reveals a card that was hidden, because a wrong guess has to
        // cost a glance rather than a download.
        Assert.Contains("class=\"dl\" data-os=\"macos\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"dl\" data-os=\"windows\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"dl\" data-os=\"linux\"", html, StringComparison.Ordinal);

        // And the recommendation card reads as a prompt rather than as an empty box.
        Assert.Contains("Pick your platform below.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDiskImageIsPromisedBecauseNoneIsBuilt()
    {
        // The specific lie this test was written for.
        Assert.DoesNotContain(".dmg", Read("index.html"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every asset name the release workflow can put on a release page.</summary>
    private static HashSet<string> Produced()
    {
        string workflow = Read(Path.Combine(".github", "workflows", "release.yml"), fromSite: false);
        var names = new HashSet<string>(StringComparer.Ordinal);

        // Read the version out of the page rather than assuming one: the site is baked
        // with whatever the latest release is, and the names carry it.
        string version = VersionPattern().Match(Read("index.html")).Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(version), "the page has no version badge to read");

        foreach (string rid in RuntimeIdentifiers)
        {
            // vpk pack --packId Detangle, per platform. These are Velopack's own names.
            if (rid.StartsWith("win", StringComparison.Ordinal))
            {
                names.Add($"Detangle-{rid}-Setup.exe");
            }
            else if (rid.StartsWith("osx", StringComparison.Ordinal))
            {
                names.Add($"Detangle-{rid}-Setup.pkg");
            }
            else
            {
                names.Add($"Detangle-{rid}.AppImage");
            }

            names.Add($"Detangle-{rid}-Portable.zip");

            // The "Archive the portable build" step, which is where detangle-lint rides.
            names.Add(rid.StartsWith("win", StringComparison.Ordinal)
                ? $"detangle-{version}-{rid}.zip"
                : $"detangle-{version}-{rid}.tar.gz");
        }

        // The "Build the .deb" step, whose architecture names are dpkg's, not .NET's.
        Assert.Contains("dpkg-deb --build", workflow, StringComparison.Ordinal);

        names.Add($"detangle_{version}_amd64.deb");
        names.Add($"detangle_{version}_arm64.deb");

        return names;
    }

    /// <summary>Every release asset the download section links to.</summary>
    private static IReadOnlyList<string> Offered() =>
        [.. DownloadLink().Matches(Read("index.html")).Select(m => m.Groups[1].Value)];

    private static string Read(string relativePath, bool fromSite = true)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = fromSite
                ? Path.Combine(directory.FullName, "site", relativePath)
                : Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{relativePath} was not found above the test binaries.");
    }

    [GeneratedRegex(@"releases/latest/download/([^""\s]+)")]
    private static partial Regex DownloadLink();

    [GeneratedRegex(@"<span id=""version"">([^<]+)</span>")]
    private static partial Regex VersionPattern();
}
