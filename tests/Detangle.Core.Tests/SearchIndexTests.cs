using Detangle.Core.Linking;
using Detangle.Core.Search;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for the FTS5 index and the field query syntax (plan.md section 6.2).</summary>
public class SearchIndexTests
{
    [Theory]
    [InlineData("attention", 1, 0, 0)]
    [InlineData("\"exact phrase\"", 0, 1, 0)]
    [InlineData("type:concept", 0, 0, 1)]
    [InlineData("type:concept attention", 1, 0, 1)]
    [InlineData("updated>2026-06-01", 0, 0, 1)]
    public void ParsesTheQuerySyntax(string query, int terms, int phrases, int filters)
    {
        SearchQuery parsed = SearchQuery.Parse(query);

        Assert.Equal(terms, parsed.Terms.Count);
        Assert.Equal(phrases, parsed.Phrases.Count);
        Assert.Equal(filters, parsed.Filters.Count);
    }

    [Fact]
    public void UnknownFieldsStayAsSearchTerms()
    {
        // A query bar that rejects input is worse than one that searches for it.
        SearchQuery parsed = SearchQuery.Parse("nonsense:value");

        Assert.Empty(parsed.Filters);
        Assert.Equal(["nonsense:value"], parsed.Terms);
    }

    [Theory]
    [InlineData("updated>2026-06-01", FieldComparison.After)]
    [InlineData("updated<2026-06-01", FieldComparison.Before)]
    [InlineData("path:wiki/entities/", FieldComparison.StartsWith)]
    [InlineData("tag:llm/agents", FieldComparison.StartsWith)]
    [InlineData("type:concept", FieldComparison.Equals)]
    public void ReadsComparisonsPerField(string query, FieldComparison expected) =>
        Assert.Equal(expected, SearchQuery.Parse(query).Filters[0].Comparison);

    [Fact]
    public void TermsBecomePrefixMatchesSoResultsAppearWhileTyping()
    {
        Assert.Equal("atten*", SearchQuery.Parse("atten").ToMatchExpression());
        Assert.Equal("\"a phrase\" AND word*", SearchQuery.Parse("\"a phrase\" word").ToMatchExpression());
        Assert.Null(SearchQuery.Parse("type:concept").ToMatchExpression());
    }

    [Fact]
    public void FindsTextAcrossTheVault()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        IReadOnlyList<SearchHit> hits = index.Search(SearchQuery.Parse("attention"), vault);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Document.RelativePath == "wiki/concepts/attention-is-all-you-need.md");
    }

    [Fact]
    public void HitsCarryTheHeadingTheyWereFoundUnder()
    {
        using SearchIndex index = BuildIndex("torture", out VaultSnapshot vault);

        IReadOnlyList<SearchHit> hits = index.Search(SearchQuery.Parse("admonition"), vault);

        Assert.Contains(hits, h => h.Heading is { Length: > 0 });
        Assert.All(hits, h => Assert.True(h.Line > 0));
    }

    [Fact]
    public void SnippetsMarkTheMatchedTerm()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        SearchHit hit = index.Search(SearchQuery.Parse("Introduced"), vault)[0];

        Assert.Contains("[", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains("]", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void NarrowsByFrontmatterType()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        IReadOnlyList<SearchHit> hits = index.Search(SearchQuery.Parse("type:concept"), vault);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("concept", h.Document.Frontmatter.Type));
    }

    [Fact]
    public void NarrowsByPathPrefix()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        IReadOnlyList<SearchHit> hits = index.Search(SearchQuery.Parse("path:wiki/entities/"), vault);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.StartsWith("wiki/entities/", h.Document.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void NarrowsByTagIncludingNestedOnes()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        Assert.NotEmpty(index.Search(SearchQuery.Parse("tag:transformers"), vault));
    }

    [Fact]
    public void ATagFilterMeansTheTagAndItsChildrenAndNothingElse()
    {
        // The tag rail is a hierarchy and this is the same rule it browses by: "llm" is
        // llm and llm/agents, but never llm-ops, which is a different tag that happens to
        // start with the same letters. A rail counting two and a search finding three
        // would replace one quiet lie with a subtler one.
        (string Path, string Content)[] files =
        [
            ("own.md", "---\ntags: [llm]\n---\n\n# Own\n\nBody.\n"),
            ("child.md", "---\ntags: [llm/agents]\n---\n\n# Child\n\nBody.\n"),
            ("neighbour.md", "---\ntags: [llm-ops]\n---\n\n# Neighbour\n\nBody.\n"),
        ];

        VaultSnapshot vault = Vault(files);

        using SearchIndex index = SearchIndex.Open(vault.RootPath, inMemory: true);

        index.Rebuild(
            vault,
            document => files.First(f => f.Path == document.RelativePath).Content,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["child.md", "own.md"],
            index.Search(SearchQuery.Parse("tag:llm"), vault)
                .Select(h => h.Document.RelativePath)
                .Order(StringComparer.Ordinal));

        Assert.Equal(
            "neighbour.md",
            Assert.Single(index.Search(SearchQuery.Parse("tag:llm-ops"), vault)).Document.RelativePath);
    }

    [Fact]
    public void CombinesTextAndFieldFilters()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        IReadOnlyList<SearchHit> narrow = index.Search(SearchQuery.Parse("type:entity attention"), vault);

        Assert.All(narrow, h => Assert.Equal("entity", h.Document.Frontmatter.Type));
    }

    [Fact]
    public void AnEmptyQueryFindsNothing()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        Assert.Empty(index.Search(SearchQuery.Parse("   "), vault));
    }

    [Fact]
    public void UpdatingADocumentReplacesItsSections()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        VaultDocument document = vault.Index.ByRelativePath("wiki/index.md").Single();

        index.Update(document, "# Rewritten\n\nCompletely different words: xylophone.\n");

        Assert.NotEmpty(index.Search(SearchQuery.Parse("xylophone"), vault));
        Assert.DoesNotContain(
            index.Search(SearchQuery.Parse("Vaswani"), vault),
            h => h.Document.RelativePath == "wiki/index.md");
    }

    [Fact]
    public void RemovingADocumentTakesItOutOfResults()
    {
        using SearchIndex index = BuildIndex("llm-wiki", out VaultSnapshot vault);

        index.Remove("wiki/concepts/attention-is-all-you-need.md");

        Assert.DoesNotContain(
            index.Search(SearchQuery.Parse("attention"), vault),
            h => h.Document.RelativePath == "wiki/concepts/attention-is-all-you-need.md");
    }

    [Fact]
    public void FrontmatterIsNotIndexedAsBodyText()
    {
        // Otherwise every page carrying "type: concept" would match a search for the word
        // "concept", and the results would be worthless.
        IReadOnlyList<(string? Heading, string Body, int Line)> sections =
        [
            .. SearchIndex.Sections("---\ntitle: A Page\nsecretword: hidden\n---\n\n# Body\n\nReal text.\n"),
        ];

        Assert.DoesNotContain(sections, s => s.Body.Contains("secretword", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.Body.Contains("Real text", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionsAreSplitByHeading()
    {
        IReadOnlyList<(string? Heading, string Body, int Line)> sections =
        [
            .. SearchIndex.Sections("Intro text.\n\n# First\n\nOne.\n\n## Second\n\nTwo.\n"),
        ];

        Assert.Equal([null, "First", "Second"], sections.Select(s => s.Heading));
        Assert.Equal([1, 3, 7], sections.Select(s => s.Line));
    }

    [Fact]
    public void IndexesEveryMarkdownFileInAVault()
    {
        using SearchIndex index = BuildIndex("mkdocs", out VaultSnapshot vault);

        Assert.True(index.SectionCount >= vault.Documents.Count(d => d.IsMarkdown));
    }

    private static VaultSnapshot Vault(params (string Path, string Content)[] files) =>
        new()
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build([.. files.Select(f => TestVault.CreateDocument(f.Path, f.Content))]),
        };

    private static SearchIndex BuildIndex(string vaultName, out VaultSnapshot vault)
    {
        vault = FixtureVaults.Load(vaultName);

        SearchIndex index = SearchIndex.Open(vault.RootPath, inMemory: true);

        index.Rebuild(
            vault,
            document => File.Exists(document.AbsolutePath) ? File.ReadAllText(document.AbsolutePath) : null);

        return index;
    }
}
