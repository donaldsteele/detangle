using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the Link Doctor (plan.md section 6.3) — the panel the whole product is
/// pointed at.
/// </summary>
public class LinkDoctorTests
{
    [Fact]
    public void ReportsBrokenLinksWithTheirLine()
    {
        Finding finding = Assert.Single(
            Examine(("page.md", "# Page\n\nSee [[attentin]].\n"), ("attention.md", "# Attention")),
            f => f.Kind == FindingKind.BrokenLink);

        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Equal(3, finding.Line);

        // Examining does not go looking for near misses: that pass costs an edit-distance
        // sweep over every name in the vault, and it is run for the finding a reader
        // actually opens rather than for the thousands they do not.
        Assert.Null(finding.SuggestedRewrite);
    }

    [Fact]
    public void SuggestingAFixFindsTheNearestName()
    {
        Finding finding = Assert.Single(
            Examine(("page.md", "# Page\n\nSee [[attentin]].\n"), ("attention.md", "# Attention")),
            f => f.Kind == FindingKind.BrokenLink);

        Finding suggested = LinkDoctor.SuggestFix(finding);

        Assert.Equal("attention", suggested.SuggestedRewrite);
        Assert.Contains(suggested.Related, d => d.RelativePath == "attention.md");
    }

    [Fact]
    public void SuggestingAFixLeavesOtherFindingKindsAlone()
    {
        Finding orphan = Assert.Single(
            Examine(("lonely.md", "# Lonely\n")), f => f.Kind == FindingKind.OrphanPage);

        Assert.Same(orphan, LinkDoctor.SuggestFix(orphan));
    }

    [Fact]
    public void ReportsAmbiguousLinksWithEveryCandidate()
    {
        Finding finding = Assert.Single(
            Examine(
                ("page.md", "[[note]]\n"),
                ("alpha/note.md", "# Alpha"),
                ("zebra/note.md", "# Zebra")),
            f => f.Kind == FindingKind.AmbiguousLink);

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(2, finding.Related.Count);
    }

    [Fact]
    public void ReportsNonCanonicalLinksWithTheRewriteTheyWant()
    {
        Finding finding = Assert.Single(
            Examine(("page.md", "[[My Target]]\n"), ("notes/my-target.md", "# My Target")),
            f => f.Kind == FindingKind.NonCanonicalLink);

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal("notes/my-target", finding.SuggestedRewrite);
    }

    [Fact]
    public void ALinkThatResolvedExactlyIsNotAFinding()
    {
        Assert.DoesNotContain(
            Examine(("page.md", "[[notes/target]]\n"), ("notes/target.md", "# Target")),
            f => f.Kind == FindingKind.NonCanonicalLink);
    }

    [Fact]
    public void ReportsOrphanPages()
    {
        IReadOnlyList<Finding> findings = Examine(
            ("hub.md", "[[spoke]]\n"),
            ("spoke.md", "# Spoke"));

        Assert.Contains(findings, f => f.Kind == FindingKind.OrphanPage && f.Document.RelativePath == "hub.md");
        Assert.DoesNotContain(
            findings, f => f.Kind == FindingKind.OrphanPage && f.Document.RelativePath == "spoke.md");
    }

    [Fact]
    public void ReportsDuplicateSlugsOnBothFiles()
    {
        IReadOnlyList<Finding> findings = Examine(
            ("a/My Note.md", "# One"),
            ("b/my-note.md", "# Two"));

        List<Finding> duplicates = [.. findings.Where(f => f.Kind == FindingKind.DuplicateSlug)];

        Assert.Equal(2, duplicates.Count);
        Assert.All(duplicates, f => Assert.Single(f.Related));
    }

    [Fact]
    public void ReportsStalePagesOnlyWhenTheyAreWellLinked()
    {
        var options = new LinkDoctorOptions
        {
            Now = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
            StaleInboundThreshold = 2,
        };

        IReadOnlyList<Finding> findings = Examine(
            options,
            ("a.md", "[[old]]\n"),
            ("b.md", "[[old]]\n"),
            ("old.md", "---\nupdated: 2024-01-01\n---\n\n# Old\n"),
            ("lonely-old.md", "---\nupdated: 2024-01-01\n---\n\n# Also old\n"));

        Assert.Contains(
            findings, f => f.Kind == FindingKind.StalePage && f.Document.RelativePath == "old.md");
        Assert.DoesNotContain(
            findings, f => f.Kind == FindingKind.StalePage && f.Document.RelativePath == "lonely-old.md");
    }

    [Fact]
    public void ReportsOversizedPages()
    {
        string long_ = string.Join('\n', Enumerable.Range(0, 500).Select(i => $"Line {i}."));

        Finding finding = Assert.Single(
            Examine(("big.md", long_)), f => f.Kind == FindingKind.OversizedPage);

        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    [Fact]
    public void AVeryLongPageIsAWarningRatherThanANote()
    {
        string longer = string.Join('\n', Enumerable.Range(0, 900).Select(i => $"Line {i}."));

        Assert.Equal(
            FindingSeverity.Warning,
            Assert.Single(Examine(("huge.md", longer)), f => f.Kind == FindingKind.OversizedPage).Severity);
    }

    [Fact]
    public void ReportsFrontmatterWithoutATitle()
    {
        Assert.Contains(
            Examine(("page.md", "---\ntype: concept\n---\n\n# Body\n")),
            f => f.Kind == FindingKind.FrontmatterIssue
                && f.Message.Contains("no title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReportsUnterminatedFrontmatter()
    {
        Assert.Contains(
            Examine(("page.md", "---\ntitle: Broken\n\n# Body\n")),
            f => f.Kind == FindingKind.FrontmatterIssue);
    }

    [Fact]
    public void SafeToFixIsOnlyTheLinksWithOneCorrectAnswer()
    {
        IReadOnlyList<Finding> findings = Examine(
            ("page.md", "[[My Target]] and [[nowhere at all]] and [[note]]\n"),
            ("my-target.md", "# Target"),
            ("a/note.md", "# A"),
            ("b/note.md", "# B"));

        List<Finding> safe = [.. LinkDoctor.SafeToFix(findings)];

        Assert.Single(safe);
        Assert.Equal(FindingKind.NonCanonicalLink, safe[0].Kind);
    }

    [Fact]
    public void ApplyingARewriteChangesOnlyTheLinkOnThatLine()
    {
        const string Content = "Intro mentions My Target in prose.\n\nSee [[My Target]] here.\n";

        IReadOnlyList<Finding> findings = Examine(
            ("page.md", Content), ("notes/my-target.md", "# My Target"));

        Finding finding = Assert.Single(findings, f => f.Kind == FindingKind.NonCanonicalLink);
        string? rewritten = LinkDoctor.ApplyRewrite(Content, finding);

        Assert.NotNull(rewritten);
        Assert.Contains("[[notes/my-target]]", rewritten, StringComparison.Ordinal);

        // The prose mention on line 1 says the same words and must not be touched.
        Assert.StartsWith("Intro mentions My Target in prose.", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoLinksToTheSamePlaceOnOneLineAreRewrittenSeparately()
    {
        // A generator writes this shape constantly, and both findings used to land on the
        // first link: it was rewritten twice and the second was never touched, while
        // "fix all safe" reported two successes.
        const string Content = "See [[My Target]] and again [[My Target]].\n";

        IReadOnlyList<Finding> findings = Examine(
            ("page.md", Content), ("notes/my-target.md", "# My Target"));

        List<Finding> links =
            [.. findings.Where(f => f.Kind == FindingKind.NonCanonicalLink)];

        Assert.Equal(2, links.Count);

        string? second = LinkDoctor.ApplyRewrite(Content, links[1]);

        Assert.Equal("See [[My Target]] and again [[notes/my-target]].\n", second);

        // And applying both, in either order, leaves no original behind.
        string? both = LinkDoctor.ApplyRewrite(LinkDoctor.ApplyRewrite(Content, links[1])!, links[0]);

        Assert.Equal("See [[notes/my-target]] and again [[notes/my-target]].\n", both);
    }

    [Fact]
    public void ALabelThatRepeatsItsOwnDestinationIsNotMistakenForIt()
    {
        // "[My_Target](My_Target)" — a shape generators write constantly. Searching the
        // line for the target text finds the label first and rewrites that, leaving a
        // link whose destination is untouched and whose visible text has become a path.
        const string Content = "See [My_Target](My_Target) here.\n";

        IReadOnlyList<Finding> findings = Examine(
            ("page.md", Content), ("notes/my-target.md", "# My Target"));

        Finding finding = Assert.Single(findings, f => f.Kind == FindingKind.NonCanonicalLink);
        string? rewritten = LinkDoctor.ApplyRewrite(Content, finding);

        Assert.NotNull(rewritten);
        Assert.StartsWith("See [My_Target](notes/", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void ARewriteThatNoLongerMatchesIsRefused()
    {
        IReadOnlyList<Finding> findings = Examine(
            ("page.md", "[[My Target]]\n"), ("my-target.md", "# Target"));

        Finding finding = Assert.Single(findings, f => f.Kind == FindingKind.NonCanonicalLink);

        // The file changed under the finding; rewriting blind would corrupt it.
        Assert.Null(LinkDoctor.ApplyRewrite("Something else entirely.\n", finding));
    }

    [Fact]
    public void ExaminesARealVaultWithoutThrowing()
    {
        LinkGraph graph = LinkGraph.Build(FixtureVaults.Load("torture"));

        IReadOnlyList<Finding> findings = LinkDoctor.Examine(graph, ReadContent);

        Assert.Contains(findings, f => f.Kind == FindingKind.BrokenLink);
        Assert.Contains(findings, f => f.Kind == FindingKind.AmbiguousLink);
        Assert.All(findings, f => Assert.NotEmpty(f.Message));
    }

    private static IReadOnlyList<Finding> Examine(params (string Path, string Content)[] files) =>
        Examine(LinkDoctorOptions.Default, files);

    private static IReadOnlyList<Finding> Examine(
        LinkDoctorOptions options, params (string Path, string Content)[] files)
    {
        var contents = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

        var documents = files
            .Select(f => TestVault.CreateDocument(f.Path, f.Content))
            .ToList();

        var vault = new VaultSnapshot
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build(documents),
        };

        return LinkDoctor.Examine(
            LinkGraph.Build(vault),
            document => contents.TryGetValue(document.RelativePath, out string? content) ? content : null,
            options);
    }

    private static string? ReadContent(VaultDocument document) =>
        File.Exists(document.AbsolutePath) ? File.ReadAllText(document.AbsolutePath) : null;
}
