using Detangle.Core.Linking;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the settled-ambiguity file (plan.md section 15.2) — the one rung of the chain
/// a person writes, and therefore the one that has to travel with the vault rather than
/// live inside one application.
/// </summary>
public class ChoiceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-choices-" + Guid.NewGuid().ToString("N")[..8]);

    public ChoiceStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ADecisionSurvivesBeingWrittenAndReadBack()
    {
        ChoiceStore store = ChoiceStore.Open(_root);

        Assert.True(store.Settle(
            TestVault.CreateDocument("wiki/concepts/attention.md", "# A\n"),
            "Transformer",
            TestVault.CreateDocument("wiki/entities/transformer.md", "# T\n"),
            candidates: 3));

        // At the vault root, beside the markdown, where a pull request will show it.
        Assert.True(File.Exists(Path.Combine(_root, ChoiceStore.FileName)));

        SettledChoice read = Assert.Single(ChoiceStore.Open(_root).All);

        Assert.Equal("wiki/concepts", read.SourceDirectory);
        Assert.Equal("Transformer", read.RawTarget);
        Assert.Equal("wiki/entities/transformer.md", read.TargetPath);
        Assert.Contains("ambiguous between 3", read.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResolverSeesTheDecisionUnderTheKeyItLooksUp()
    {
        ChoiceStore store = ChoiceStore.Open(_root);

        store.Settle(
            TestVault.CreateDocument("wiki/a.md", "# A\n"),
            "Target",
            TestVault.CreateDocument("wiki/b.md", "# B\n"));

        Assert.Equal(
            "wiki/b.md",
            store.ForResolver[LinkResolver.ChoiceKey("wiki", "Target")]);
    }

    [Fact]
    public void RevokingTheLastDecisionLeavesTheVaultAsItWasFound()
    {
        ChoiceStore store = ChoiceStore.Open(_root);

        store.Settle(
            TestVault.CreateDocument("a.md", "# A\n"),
            "Target",
            TestVault.CreateDocument("b.md", "# B\n"));

        Assert.True(store.Forget(store.All[0]));
        Assert.Empty(store.ForResolver);

        // Not a file with a header and nothing under it.
        Assert.False(File.Exists(Path.Combine(_root, ChoiceStore.FileName)));
    }

    [Fact]
    public void AnAnchorInTheLinkIsNotMistakenForAComment()
    {
        // "[[Setup#Install]]" is an ordinary ambiguous link. Reading everything after its
        // hash as a comment would silently record a decision about a different link.
        SettledChoice choice = Assert.IsType<SettledChoice>(
            ChoiceStore.Parse("wiki | Setup#Install -> wiki/setup/index.md  # settled 2026-08-22"));

        Assert.Equal("Setup#Install", choice.RawTarget);
        Assert.Equal("wiki/setup/index.md", choice.TargetPath);
        Assert.Equal("settled 2026-08-22", choice.Note);
    }

    [Fact]
    public void APageAtTheVaultRootIsWrittenWithADotRatherThanAnEmptyField()
    {
        ChoiceStore store = ChoiceStore.Open(_root);

        store.Settle(
            TestVault.CreateDocument("index.md", "# I\n"),
            "Target",
            TestVault.CreateDocument("b.md", "# B\n"));

        Assert.Contains(". | Target ->", store.Serialize(), StringComparison.Ordinal);

        // And comes back as the root, not as a folder literally named ".".
        Assert.Equal(string.Empty, ChoiceStore.Open(_root).All[0].SourceDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# just a comment")]
    [InlineData("no separators at all")]
    [InlineData("wiki | missing an arrow")]
    [InlineData("wiki | Target ->")]
    [InlineData("Target -> b.md | backwards")]
    public void ALineSomebodyMistypedIsSkippedRatherThanThrownOver(string line) =>
        Assert.Null(ChoiceStore.Parse(line));

    [Fact]
    public void ADetachedStoreRemembersForTheSessionAndSaysItCannotKeepIt()
    {
        // The browser head: a decision lasts as long as the tab, and claiming a save into
        // a filesystem that disappears with it would be worse than saying so.
        ChoiceStore store = ChoiceStore.Detached();

        Assert.False(store.IsPersistent);
        Assert.False(store.Settle(
            TestVault.CreateDocument("a.md", "# A\n"),
            "Target",
            TestVault.CreateDocument("b.md", "# B\n")));

        Assert.Single(store.ForResolver);
    }

    [Fact]
    public void TheFileIsSortedSoItDoesNotChurnBetweenSaves()
    {
        ChoiceStore store = ChoiceStore.Open(_root);

        foreach (string target in (string[])["Zebra", "Alpha", "Middle"])
        {
            store.Settle(
                TestVault.CreateDocument("wiki/a.md", "# A\n"),
                target,
                TestVault.CreateDocument("wiki/b.md", "# B\n"));
        }

        Assert.Equal(["Alpha", "Middle", "Zebra"], store.All.Select(c => c.RawTarget));
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
