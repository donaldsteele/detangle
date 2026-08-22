using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for remembered disambiguation choices (plan.md section 15.2) — the one step in
/// the resolution chain the reader writes.
/// </summary>
public class VaultStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-choice-" + Guid.NewGuid().ToString("N")[..8]);

    public VaultStateTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "zebra"));

        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[note]].\n");
        File.WriteAllText(Path.Combine(_root, "alpha", "note.md"), "# Alpha note\n");
        File.WriteAllText(Path.Combine(_root, "zebra", "note.md"), "# Zebra note\n");
    }

    [Fact]
    public void WithoutAChoiceTheAmbiguityIsReported()
    {
        ShellViewModel shell = Open();

        Finding finding = Assert.Single(shell.Findings, f => f.Kind == FindingKind.AmbiguousLink);

        Assert.Equal(2, finding.Related.Count);
    }

    [Fact]
    public void SettlingALinkMakesTheChainFollowTheReader()
    {
        ShellViewModel shell = Open();

        LinkResolution resolution = Ambiguous(shell);

        Assert.Equal("alpha/note.md", resolution.Target?.RelativePath);

        shell.Settle(resolution, Document(shell, "zebra/note.md"));

        LinkResolution settled = Resolution(shell);

        Assert.Equal("zebra/note.md", settled.Target?.RelativePath);
        Assert.Equal(ResolutionRule.RememberedChoice, settled.Rule);

        // It stops being a finding, because it is no longer an open question.
        Assert.DoesNotContain(shell.Findings, f => f.Kind == FindingKind.AmbiguousLink);
    }

    [Fact]
    public void AChoiceOutlivesTheSession()
    {
        ShellViewModel first = Open();
        first.Settle(Ambiguous(first), Document(first, "zebra/note.md"));

        Assert.True(File.Exists(Path.Combine(_root, ".detangle", "state.json")));

        // A second reader of the same vault gets the first one's decision.
        ShellViewModel second = Open();

        Assert.Equal("zebra/note.md", Resolution(second).Target?.RelativePath);
    }

    [Fact]
    public void AChoiceSurvivesARescan()
    {
        ShellViewModel shell = Open();
        shell.Settle(Ambiguous(shell), Document(shell, "zebra/note.md"));

        // A file changing outside the app rebuilds the graph from a fresh scan; building
        // it without the choices is how a watcher would quietly undo every decision.
        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[note]] still.\n");
        shell.OpenVault(_root);

        Assert.Equal("zebra/note.md", Resolution(shell).Target?.RelativePath);
    }

    [Fact]
    public void ADetachedCopyRemembersForTheSessionAndSaysSo()
    {
        ShellViewModel shell = new();
        shell.OpenVault(_root, isDetachedCopy: true);

        shell.Settle(Ambiguous(shell), Document(shell, "zebra/note.md"));

        // The choice applies here...
        Assert.Equal("zebra/note.md", Resolution(shell).Target?.RelativePath);

        // ...and is not claimed to have been saved anywhere. The .detangle folder itself
        // may well exist: the search cache lives there too.
        Assert.False(File.Exists(Path.Combine(_root, ".detangle", "state.json")));
        Assert.Contains("until this tab is closed", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableChoiceFileIsNoChoicesRatherThanNoVault()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".detangle"));
        File.WriteAllText(Path.Combine(_root, ".detangle", "state.json"), "{ this is not json");

        ShellViewModel shell = Open();

        Assert.Contains(shell.Findings, f => f.Kind == FindingKind.AmbiguousLink);
    }

    private ShellViewModel Open()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(_root);

        return shell;
    }

    private static LinkResolution Ambiguous(ShellViewModel shell)
    {
        Finding finding = Assert.Single(shell.Findings, f => f.Kind == FindingKind.AmbiguousLink);

        return finding.Resolution!;
    }

    private static LinkResolution Resolution(ShellViewModel shell)
    {
        shell.Open(Document(shell, "index.md"));

        return Assert.Single(
            shell.ActiveTab!.Rendered!.Resolutions,
            r => r.Link.RawTarget == "note");
    }

    private static VaultDocument Document(ShellViewModel shell, string relativePath) =>
        shell.Vault!.Documents.First(d => d.RelativePath == relativePath);

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
