using Detangle.Core.Graph;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the shell's half of the graph view (plan.md section 6.4): which filters
/// produce which model, and what clicking a node does.
/// </summary>
public class GraphViewTests
{
    [Fact]
    public void TheGraphIsNotBuiltUntilItIsShown()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        // Opening a vault already costs a scan, a resolve, an index and an examination;
        // laying out a picture nobody has asked to see is not on that list.
        Assert.Empty(shell.GraphModel.Nodes);

        shell.ToggleGraphCommand.Execute(null);

        Assert.True(shell.IsGraphVisible);
        Assert.NotEmpty(shell.GraphModel.Nodes);
        Assert.Contains("nodes", shell.GraphSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingAFilterRebuildsTheGraphOnlyWhileItIsShown()
    {
        ShellViewModel shell = OpenVault("llm-wiki");

        shell.GraphFolderFilter = "wiki";

        Assert.Empty(shell.GraphModel.Nodes);

        shell.ToggleGraphCommand.Execute(null);

        Assert.NotEmpty(shell.GraphModel.Nodes);
        Assert.All(
            shell.GraphModel.Nodes.Where(n => n.Kind == GraphNodeKind.Page),
            n => Assert.StartsWith("wiki/", n.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void LocalModeCentresOnTheOpenPage()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        shell.ToggleGraphCommand.Execute(null);

        int whole = shell.GraphModel.Nodes.Count;

        shell.GraphHops = 1;
        shell.IsGraphLocal = true;

        Assert.NotEmpty(shell.GraphModel.Nodes);
        Assert.True(
            shell.GraphModel.Nodes.Count < whole,
            "one hop from the open page should be smaller than the whole vault");
        Assert.Contains(shell.GraphModel.Nodes, n => n.Id == shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void HidingMissingTargetsRemovesThem()
    {
        ShellViewModel shell = OpenVault("torture");
        shell.ToggleGraphCommand.Execute(null);

        Assert.Contains(shell.GraphModel.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);

        shell.GraphShowsMissingTargets = false;

        Assert.DoesNotContain(shell.GraphModel.Nodes, n => n.Kind == GraphNodeKind.MissingTarget);
    }

    [Fact]
    public void ClickingAPageOpensItAndLeavesTheGraph()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        shell.ToggleGraphCommand.Execute(null);

        GraphNode node = shell.GraphModel.Nodes.First(
            n => n.Kind == GraphNodeKind.Page && n.Document is not null);

        shell.OpenNode(node);

        Assert.False(shell.IsGraphVisible);
        Assert.Equal(node.Id, shell.ActiveTab!.Document.RelativePath);
    }

    [Fact]
    public void ClickingAMissingTargetSaysSoRatherThanOpeningAnything()
    {
        ShellViewModel shell = OpenVault("torture");
        shell.ToggleGraphCommand.Execute(null);

        VaultDocument? before = shell.ActiveTab?.Document;

        shell.OpenNode(shell.GraphModel.Nodes.First(n => n.Kind == GraphNodeKind.MissingTarget));

        Assert.True(shell.IsGraphVisible);
        Assert.Equal(before?.RelativePath, shell.ActiveTab?.Document.RelativePath);
        Assert.Contains("matches no file", shell.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickingAFolderClusterDrillsIntoIt()
    {
        ShellViewModel shell = OpenVault("llm-wiki");
        shell.ToggleGraphCommand.Execute(null);

        var cluster = new GraphNode(
            0, "wiki", "wiki", GraphNodeKind.Cluster, null, null, "wiki", [], 0, 0, Weight: 4);

        shell.OpenNode(cluster);

        Assert.Equal("wiki", shell.GraphFolderFilter);
        Assert.All(
            shell.GraphModel.Nodes.Where(n => n.Kind == GraphNodeKind.Page),
            n => Assert.StartsWith("wiki/", n.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void AGraphWithNoVaultIsEmptyRatherThanNull()
    {
        var shell = new ShellViewModel();

        shell.RebuildGraph();

        Assert.Empty(shell.GraphModel.Nodes);
        Assert.Empty(shell.GraphSummary);
    }

    private static ShellViewModel OpenVault(string vaultName)
    {
        var shell = new ShellViewModel();

        shell.OpenVault(Path.Combine(FixtureRoot, "vaults", vaultName));

        return shell;
    }

    private static string FixtureRoot { get; } = FindFixtures();

    private static string FindFixtures()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("tests/fixtures was not found above the test binaries.");
    }
}
