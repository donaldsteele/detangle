using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the left rail's tree: that each flavor's stated navigation is read, and
/// that nothing a stated navigation forgets goes missing (plan.md section 6.1).
/// </summary>
public class NavigationTreeTests
{
    [Fact]
    public void ReadsTheMkDocsNav()
    {
        NavigationTreeBuilder.Result result = Build("mkdocs");

        Assert.Equal(NavigationSource.MkDocsNav, result.Source);
        Assert.Equal("Home", result.Roots[0].Title);
        Assert.Equal("docs/index.md", result.Roots[0].Document?.RelativePath);
        Assert.Equal("docs/guide/index.md", result.Roots[1].Document?.RelativePath);
    }

    [Fact]
    public void ReadsSummaryMdForGitBook()
    {
        NavigationTreeBuilder.Result result = Build("gitbook");

        Assert.Equal(NavigationSource.Summary, result.Source);
        Assert.Contains(result.Roots, n => n.Document?.RelativePath == "README.md");
        Assert.Contains(result.Roots, n => n.Document?.RelativePath == "chapter/README.md");
    }

    [Fact]
    public void ReadsSummaryMdForMdBook()
    {
        NavigationTreeBuilder.Result result = Build("mdbook");

        Assert.Equal(NavigationSource.Summary, result.Source);
        Assert.Contains(result.Roots, n => n.Document?.RelativePath == "src/ch01.md");
    }

    [Fact]
    public void ReadsTheDocsifySidebar()
    {
        NavigationTreeBuilder.Result result = Build("docsify");

        Assert.Equal(NavigationSource.Sidebar, result.Source);
        Assert.Contains(result.Roots, n => n.Document?.RelativePath == "guide.md");
    }

    [Fact]
    public void ReadsTheDeepWikiPageList()
    {
        NavigationTreeBuilder.Result result = Build("deepwiki");

        Assert.Equal(NavigationSource.DeepWikiPages, result.Source);
        // Both pages are listed, so there is nothing left over to append.
        Assert.Equal(["Overview", "Architecture"], result.Roots.Select(n => n.Title));
    }

    [Fact]
    public void ReadsAnLlmWikiIndexPage()
    {
        NavigationTreeBuilder.Result result = Build("llm-wiki");

        Assert.Equal(NavigationSource.IndexPage, result.Source);
        Assert.Contains(
            result.Roots,
            n => n.Document?.RelativePath == "wiki/concepts/attention-is-all-you-need.md");
    }

    [Fact]
    public void FallsBackToTheFileSystemWhenNothingStatesAnOrder()
    {
        NavigationTreeBuilder.Result result = Build("torture");

        Assert.Equal(NavigationSource.FileSystem, result.Source);
        Assert.Contains(result.Roots, n => n.IsGroup && n.Title == "folder");
        Assert.Contains(result.Roots, n => n.Document?.RelativePath == "torture.md");
    }

    [Fact]
    public void DocumentsMissingFromAStatedNavigationAreStillReachable()
    {
        // A page the author forgot to list is exactly what this app should surface, not
        // hide behind the navigation file's omission.
        NavigationTreeBuilder.Result result = Build("gitbook");

        NavigationNode remainder = Assert.Single(result.Roots, n => n.Title == "Not in navigation");

        Assert.Contains(Documents(remainder), path => path == "chapter/page.md");
    }

    [Fact]
    public void DocumentsMissingFromAStatedNavigationKeepTheirFolders()
    {
        // The leftovers are usually most of the vault - a stated navigation listing a
        // hundred pages in a repository holding five hundred is ordinary - so listing them
        // flat produces a rail with hundreds of siblings and no structure, which is what a
        // real wiki of 462 documents showed: 359 of them in one flat list.
        NavigationTreeBuilder.Result result = Build("gitbook");

        NavigationNode remainder = Assert.Single(result.Roots, n => n.Title == "Not in navigation");
        NavigationNode chapter = Assert.Single(remainder.Children, n => n.Title == "chapter");

        Assert.True(chapter.IsGroup);
        Assert.Contains(chapter.Children, n => n.Document?.RelativePath == "chapter/page.md");

        // And a page at the root stays at the root rather than being pushed into a folder
        // that does not exist.
        Assert.DoesNotContain(remainder.Children, n => n.IsGroup && n.Title.Length == 0);
    }

    /// <summary>Every document reachable under a node, at any depth.</summary>
    private static IEnumerable<string> Documents(NavigationNode node)
    {
        if (node.Document is { } document)
        {
            yield return document.RelativePath;
        }

        foreach (string path in node.Children.SelectMany(Documents))
        {
            yield return path;
        }
    }

    [Fact]
    public void TheFileSystemTreeNestsDirectories()
    {
        NavigationTreeBuilder.Result result = Build("obsidian");

        NavigationNode notes = Assert.Single(result.Roots, n => n.Title == "notes");

        Assert.True(notes.IsGroup);
        Assert.Contains(notes.Children, n => n.Document?.RelativePath == "notes/Alpha Note.md");
    }

    private static NavigationTreeBuilder.Result Build(string vaultName)
    {
        VaultSnapshot vault = FixtureVaults.Load(vaultName);

        return NavigationTreeBuilder.Build(
            vault,
            document => File.Exists(document.AbsolutePath) ? File.ReadAllText(document.AbsolutePath) : null);
    }
}
