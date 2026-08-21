using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// The demo vault in samples/ makes specific claims — this link resolves by that rule,
/// that one is ambiguous, this one is broken on purpose. It ships in the WASM demo and
/// the website quotes it, so the claims are asserted here rather than trusted.
/// </summary>
public class SampleVaultTests
{
    [Theory]
    [InlineData("index.md", "Attention Is All You Need", "entities/attention-is-all-you-need.md", ResolutionRule.NormalizedName)]
    [InlineData("entities/attention-is-all-you-need.md", "self attention", "concepts/self-attention.md", ResolutionRule.NormalizedName)]
    [InlineData("entities/attention-is-all-you-need.md", "Vaswani", "entities/vaswani.md", ResolutionRule.NoteRelativePath)]
    [InlineData("index.md", "wiki/schema", "wiki/schema.md", ResolutionRule.ExactVaultPath)]
    [InlineData("index.md", "wiki/setup", "wiki/setup/index.md", ResolutionRule.FolderIndex)]
    public void TheAdvertisedLinksResolveByTheAdvertisedRule(
        string source, string target, string expected, ResolutionRule rule)
    {
        LinkResolution resolution = Resolve(source, target);

        Assert.Equal(expected, resolution.Target?.RelativePath);
        Assert.Equal(rule, resolution.Rule);
    }

    [Fact]
    public void TheSameLinkTextResolvesByDifferentRulesFromDifferentPages()
    {
        // Not a quirk - the point. "[[self attention]]" is a sibling of the page in
        // concepts/ and a normalized-name match from anywhere else, and the chain is
        // supposed to prefer the nearer answer. Naming the source page is what makes any
        // assertion about a rule meaningful.
        Assert.Equal(
            ResolutionRule.NoteRelativePath,
            Resolve("concepts/transformer.md", "self attention").Rule);

        Assert.Equal(
            ResolutionRule.NormalizedName,
            Resolve("entities/attention-is-all-you-need.md", "self attention").Rule);
    }

    [Fact]
    public void TheAmbiguousLinkIsStillAmbiguous()
    {
        LinkResolution resolution = Resolve("index.md", "Transformer");

        Assert.True(resolution.IsAmbiguous);
        Assert.Equal(2, resolution.Candidates.Count);

        // The ambiguity policy picks the shortest path, then alphabetical.
        Assert.Equal("concepts/transformer.md", resolution.Target?.RelativePath);
    }

    [Fact]
    public void TheBrokenLinkIsStillBroken()
    {
        LinkResolution resolution = Resolve("index.md", "Dose Response");

        Assert.True(resolution.IsUnresolved);
        Assert.Empty(resolution.Suggestions);
    }

    [Fact]
    public void TheAnchorWrittenInProseResolves()
    {
        VaultSnapshot vault = Vault();
        VaultDocument index = vault.Index.ByRelativePath("index.md").Single();

        LinkResolution resolution = vault.CreateResolver()
            .ResolveAll(index)
            .Single(r => r.Link.Anchor is not null && r.Link.RawTarget.Contains("getting", StringComparison.Ordinal));

        Assert.Equal("wiki/getting-started.md", resolution.Target?.RelativePath);
        Assert.True(resolution.Anchor.IsResolved);
    }

    [Fact]
    public void EveryLinkExceptTheOneBrokenOnPurposeResolves()
    {
        LinkGraph graph = LinkGraph.Build(Vault());

        List<LinkResolution> unresolved = [.. graph.Resolutions.Where(r => r.IsUnresolved)];

        Assert.All(
            unresolved,
            r => Assert.Contains("dose", LinkNormalizer.Normalize(r.Link.RawTarget), StringComparison.Ordinal));
    }

    [Fact]
    public void TheDemoShowsOffTheThingsTheWebsiteSaysItDoes()
    {
        VaultSnapshot vault = Vault();

        // A frontmatter reference is a link, which is what the Vaswani page is there to
        // demonstrate; other viewers drop these and under-report the graph.
        LinkGraph graph = LinkGraph.Build(vault);
        VaultDocument paper = vault.Index.ByRelativePath("entities/attention-is-all-you-need.md").Single();

        Assert.Contains(
            graph.BacklinksTo(paper),
            b => b.Source.RelativePath == "entities/vaswani.md"
                && b.Resolution.Link.Syntax == LinkSyntax.Frontmatter);

        // And the diagram fences the demo is meant to render are actually there.
        string concepts = File.ReadAllText(
            vault.Index.ByRelativePath("concepts/self-attention.md").Single().AbsolutePath);

        Assert.Contains("```mermaid", concepts, StringComparison.Ordinal);
        Assert.Contains(
            "```dbml",
            File.ReadAllText(vault.Index.ByRelativePath("wiki/schema.md").Single().AbsolutePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDemoVaultHasNoAccidentalProblems()
    {
        LinkGraph graph = LinkGraph.Build(Vault());

        IReadOnlyList<Finding> findings = LinkDoctor.Examine(
            graph,
            d => File.Exists(d.AbsolutePath) ? File.ReadAllText(d.AbsolutePath) : null);

        // Exactly two problems, both deliberate: one page nobody wrote, and one name that
        // matches two files. Anything else is a mistake in a folder the website invites
        // people to judge the product by.
        Assert.All(
            findings.Where(f => f.Kind == FindingKind.BrokenLink),
            f => Assert.Contains("Dose Response", f.Message, StringComparison.Ordinal));

        Assert.All(
            findings.Where(f => f.Kind == FindingKind.AmbiguousLink),
            f => Assert.Contains("Transformer", f.Message, StringComparison.Ordinal));

        // Two duplicate slugs are expected. transformer.md twice is the collision that
        // makes the ambiguous link ambiguous. index.md twice is not a mistake either: a
        // folder index has to be called index.md for the folder-index rule to find it, so
        // every wiki with subfolders has several.
        Assert.All(
            findings.Where(f => f.Kind == FindingKind.DuplicateSlug),
            f => Assert.Contains(
                Path.GetFileName(f.Document.RelativePath),
                (string[])["transformer.md", "index.md"],
                StringComparer.Ordinal));

        Assert.Contains(findings, f => f.Kind == FindingKind.BrokenLink);
        Assert.Contains(findings, f => f.Kind == FindingKind.AmbiguousLink);
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.FrontmatterIssue);
    }

    [Fact]
    public void TheDocumentationVaultHasNoBrokenLinks()
    {
        // docs/ is published by Detangle's own exporter and is also the app's built-in
        // help, so a link that does not resolve there is embarrassing twice over.
        LinkGraph graph = LinkGraph.Build(VaultScanner.Scan(FindFolder("docs")));

        Assert.NotEmpty(graph.Resolutions);
        Assert.DoesNotContain(graph.Resolutions, r => r.IsUnresolved);
    }

    /// <summary>
    /// Resolves one link, from a named page. The page matters: the chain prefers a
    /// nearer answer, so the same link text legitimately resolves by different rules
    /// depending on where it was written. Taking "whichever page came first" made this
    /// suite depend on the order the filesystem happened to enumerate in.
    /// </summary>
    private static LinkResolution Resolve(string sourcePath, string target)
    {
        VaultSnapshot vault = Vault();
        VaultDocument source = vault.Index.ByRelativePath(sourcePath).Single();

        foreach (LinkResolution resolution in vault.CreateResolver().ResolveAll(source))
        {
            if (string.Equals(resolution.Link.RawTarget, target, StringComparison.Ordinal))
            {
                return resolution;
            }
        }

        throw new InvalidOperationException($"no link to \"{target}\" in {sourcePath}.");
    }

    private static VaultSnapshot Vault() => VaultScanner.Scan(FindFolder("samples"));

    private static string FindFolder(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, name);

            if (File.Exists(Path.Combine(candidate, "index.md")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"{name}/ was not found above the test binaries.");
    }
}
