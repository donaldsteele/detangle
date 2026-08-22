using Detangle.Core.History;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Captures the regeneration-diff numbers the website quotes (plan.md section 9).
/// <para>
/// Every number on the site is checkable against the program that produced it; an invented
/// one would be the first lie there. So the figures in the "Regeneration" section come
/// from here, over a copy of <c>samples/</c>, with one page moved the way a generator
/// moves it — and this fails if the shape of that answer ever changes.
/// </para>
/// </summary>
public class SampleVaultDeltaTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-sample-delta-" + Guid.NewGuid().ToString("N")[..8]);

    public SampleVaultDeltaTests()
    {
        var source = new DirectoryInfo(SampleVault());

        foreach (FileInfo file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source.FullName, file.FullName);
            string destination = Path.Combine(_root, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            file.CopyTo(destination);
        }
    }

    [Fact]
    public void MovingAPageOutOfItsFolderPushesItsLinksDownTheLadder()
    {
        var shell = new ShellViewModel();

        shell.OpenVault(_root);
        shell.MarkBaseline();

        // The regeneration: the generator emits self-attention.md at the vault root this
        // time instead of under concepts/. Nothing else changes.
        File.Move(
            Path.Combine(_root, "concepts", "self-attention.md"),
            Path.Combine(_root, "self-attention.md"));

        shell.Reconcile();

        // The sentence the website prints under the ladder figure.
        Assert.Equal("1 renamed page \u00b7 5 links now need a later rule", shell.ChangeSummary);

        // Moving one page is a rename, not a page lost and a page gained.
        Assert.Equal("self-attention.md", Assert.Single(shell.Delta.Renamed).Value);
        Assert.Empty(shell.Delta.Added);
        Assert.Empty(shell.Delta.Removed);

        List<LinkChange> degraded = [.. shell.Delta.Links.Where(l => l.Kind == LinkChangeKind.Degraded)];

        Assert.Equal(5, degraded.Count);

        // The three distinct falls the figure draws, as rung numbers.
        Assert.Equal(
            [(1, 3), (2, 5), (2, 6)],
            degraded
                .Select(l => ((int)l.Before.Rule, (int)l.After.Rule))
                .Distinct()
                .OrderBy(pair => pair)
                .ToArray());

        // And the claim the section is built on: not one of them stopped working.
        Assert.DoesNotContain(shell.Delta.Links, l => l.Kind == LinkChangeKind.Broke);
    }

    private static string SampleVault()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "samples");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("samples/ was not found above the test binaries.");
    }

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
