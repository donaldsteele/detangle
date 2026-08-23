using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Repair;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for planning a repair without applying one (plan.md section 6.3). The property
/// that matters most is the one at the end: nothing here writes to a vault.
/// </summary>
public class VaultRepairTests
{
    [Fact]
    public void ANonCanonicalLinkBecomesAHunkThatNamesTheRuleThatResolvedIt()
    {
        PatchSet plan = Plan(
            ("index.md", "# Index\n\nSee [[My Target]].\n"),
            ("my-target.md", "# My Target\n"));

        FilePatch patch = Assert.Single(plan.Patches);
        Hunk hunk = Assert.Single(patch.Hunks);

        Assert.Equal("index.md", patch.RelativePath);
        Assert.Equal(3, hunk.Line);
        Assert.Equal("See [[My Target]].", hunk.Before);
        Assert.Equal("See [[my-target]].", hunk.After);

        // The provenance is the product: the diff says why, not only what.
        Assert.Equal("My Target", hunk.RawTarget);
        Assert.NotEmpty(hunk.Rule);
    }

    [Fact]
    public void TwoLinksOnOneLineDoNotShiftEachOther()
    {
        // A canonical target is rarely the same length as what it replaces, so a rewrite
        // that worked left to right would move the second link out from under its column.
        PatchSet plan = Plan(
            ("index.md", "# Index\n\n[[My Target]] and [[Another Page]].\n"),
            ("my-target.md", "# My Target\n"),
            ("another-page.md", "# Another Page\n"));

        Hunk hunk = Assert.Single(Assert.Single(plan.Patches).Hunks);

        Assert.Equal("[[my-target]] and [[another-page]].", hunk.After);

        // One hunk, two links. Anything reporting "what will change" to a person has to
        // say two: a confirm card that offered to rewrite "1 link" here would be counting
        // the lines and calling them links.
        Assert.Equal(2, hunk.Links);
        Assert.Equal(2, plan.LinkCount);
        Assert.Equal(1, plan.HunkCount);
        Assert.Equal("2 links in 1 file", plan.Summary());
    }

    [Fact]
    public void HunksAreReportedInFileOrderEvenThoughTheyArePlannedBottomUp()
    {
        PatchSet plan = Plan(
            ("index.md", "# Index\n\n[[My Target]]\n\nmore\n\n[[Another Page]]\n"),
            ("my-target.md", "# My Target\n"),
            ("another-page.md", "# Another Page\n"));

        IReadOnlyList<Hunk> hunks = Assert.Single(plan.Patches).Hunks;

        Assert.Equal(2, hunks.Count);
        Assert.True(hunks[0].Line < hunks[1].Line);
    }

    [Fact]
    public void TheSafePolicyLeavesAGuessAtABrokenLinkAlone()
    {
        (string, string)[] files =
        [
            ("index.md", "# Index\n\nSee [[my-targt]].\n"),
            ("my-target.md", "# My Target\n"),
        ];

        // Nothing mechanical about an edit-distance guess; it is a plan for a person.
        Assert.True(Plan(RepairPolicy.Safe, files).IsEmpty);

        PatchSet everything = Plan(new RepairPolicy(RepairScope.All), files);

        Assert.Equal("See [[my-target]].", Assert.Single(Assert.Single(everything.Patches).Hunks).After);
    }

    [Fact]
    public void AFileThatCannotBeReadIsSkippedRatherThanFailingThePlan()
    {
        VaultSnapshot vault = Vault(
            ("index.md", "# Index\n\nSee [[My Target]].\n"),
            ("my-target.md", "# My Target\n"));

        Assert.True(VaultRepair.Plan(Findings(vault), _ => null).IsEmpty);
    }

    [Fact]
    public void ThePlanRecordsWhatItWasComputedAgainstSoItCannotBeAppliedBlind()
    {
        PatchSet plan = Plan(
            ("index.md", "# Index\n\nSee [[My Target]].\n"),
            ("my-target.md", "# My Target\n"));

        // A patch computed against one version of a file and applied to another is how a
        // repair silently corrupts a page, and this corpus is regenerated wholesale.
        Assert.NotEmpty(Assert.Single(plan.Patches).ContentHash);
    }

    [Fact]
    public void TheDiffIsUnifiedAndCarriesTheRungAboveEachHunk()
    {
        string diff = Plan(
                ("index.md", "# Index\n\nSee [[My Target]].\n"),
                ("my-target.md", "# My Target\n"))
            .ToUnifiedDiff();

        Assert.Contains("--- a/index.md", diff, StringComparison.Ordinal);
        Assert.Contains("+++ b/index.md", diff, StringComparison.Ordinal);
        Assert.Contains("@@ -3,1 +3,1 @@ resolved by", diff, StringComparison.Ordinal);
        Assert.Contains("-See [[My Target]].", diff, StringComparison.Ordinal);
        Assert.Contains("+See [[my-target]].", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AVaultWithNothingToRepairPlansNothing()
    {
        PatchSet plan = Plan(
            ("index.md", "# Index\n\nSee [[my-target]].\n"),
            ("my-target.md", "# My Target\n"));

        Assert.True(plan.IsEmpty);
        Assert.Equal("Nothing to repair.", plan.Summary());
        Assert.Empty(plan.ToUnifiedDiff());
    }

    private static PatchSet Plan(params (string Path, string Content)[] files) =>
        Plan(RepairPolicy.Safe, files);

    private static PatchSet Plan(RepairPolicy policy, params (string Path, string Content)[] files)
    {
        VaultSnapshot vault = Vault(files);
        var contents = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

        return VaultRepair.Plan(
            Findings(vault),
            document => contents.GetValueOrDefault(document.RelativePath),
            policy);
    }

    private static IReadOnlyList<Finding> Findings(VaultSnapshot vault) =>
    [
        .. LinkDoctor.Examine(LinkGraph.Build(vault), _ => null).Select(LinkDoctor.SuggestFix),
    ];

    private static VaultSnapshot Vault(params (string Path, string Content)[] files) =>
        new()
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build([.. files.Select(f => TestVault.CreateDocument(f.Path, f.Content))]),
        };
}
