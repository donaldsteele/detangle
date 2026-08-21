using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Guards on the release workflow (plan.md section 10, phase 8).
/// <para>
/// The workflow itself can only be proven by tagging a release, which is not something a
/// test suite can do. What can be checked here is that it still says what the plan says
/// it should: six runtime identifiers, each built on a runner that can sign for it. A
/// dropped matrix entry is a release that quietly ships five platforms.
/// </para>
/// </summary>
public class ReleaseWorkflowTests
{
    private static readonly string[] RuntimeIdentifiers =
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    [Fact]
    public void EveryRuntimeIdentifierIsStillInTheMatrix()
    {
        string workflow = Workflow();

        Assert.All(
            RuntimeIdentifiers,
            rid => Assert.Contains($"- rid: {rid}", workflow, StringComparison.Ordinal));
    }

    [Fact]
    public void MacBuildsRunOnMacBecauseNotarizationNeedsIt()
    {
        string workflow = Workflow();

        // And the Intel build runs on an Intel runner, so the .app that gets signed is
        // the one that was built and tested.
        Assert.Contains("runner: macos-13", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: macos-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("notarytool", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseRefusesToShipAPartialSetOfArtifacts()
    {
        string workflow = Workflow();

        Assert.Contains("no artifact for", workflow, StringComparison.Ordinal);
        Assert.Contains("fail_on_unmatched_files: true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublishFlagsAreTheOnesTheStackSectionPinned()
    {
        string workflow = Workflow();

        // Self-contained, trimmed, single file — and deliberately not Native AOT, whose
        // failures under Avalonia's reflection-heavy binding layer are silent.
        Assert.Contains("--self-contained true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:PublishTrimmed=true", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishAot", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxShipsAnAppImageAndADeb()
    {
        string workflow = Workflow();

        Assert.Contains("Pack the AppImage", workflow, StringComparison.Ordinal);
        Assert.Contains("dpkg-deb --build", workflow, StringComparison.Ordinal);

        // The WebView backend's libraries are optional at run time, so they must not be
        // hard dependencies of the package.
        Assert.Contains("Recommends: libwebkit2gtk-4.1-0", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Depends: libwebkit2gtk", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksumsArePublishedWithTheRelease()
    {
        Assert.Contains("sha256sum", Workflow(), StringComparison.Ordinal);
    }

    private static string Workflow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, ".github", "workflows", "release.yml");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(".github/workflows/release.yml was not found above the test binaries.");
    }
}
