using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the tag browser's hierarchy (plan.md section 6.1), and specifically for the
/// rule the rail and the search box have to agree on: a tag names itself and everything
/// nested under it.
/// </summary>
public class TagTreeTests
{
    [Fact]
    public void NestedTagsBecomeAHierarchy()
    {
        IReadOnlyList<TagNode> tags = Build(
            ("a.md", "---\ntags: [llm/agents]\n---\n\n# A\n"),
            ("b.md", "---\ntags: [llm]\n---\n\n# B\n"));

        TagNode llm = Assert.Single(tags);

        Assert.Equal("llm", llm.FullTag);
        Assert.Equal("agents", Assert.Single(llm.Children).Segment);
    }

    [Fact]
    public void ATagListsThePagesUnderItAsWellAsItsOwn()
    {
        IReadOnlyList<TagNode> tags = Build(
            ("a.md", "---\ntags: [llm/agents]\n---\n\n# A\n"),
            ("b.md", "---\ntags: [llm]\n---\n\n# B\n"),
            ("c.md", "---\ntags: [other]\n---\n\n# C\n"));

        TagNode llm = tags.Single(t => t.FullTag == "llm");

        // The tree shows a count of two; this is what selecting it has to produce.
        Assert.Equal(2, llm.TotalCount);
        Assert.Equal(["a.md", "b.md"], llm.AllDocuments.Select(d => d.RelativePath));
    }

    [Fact]
    public void APageCarryingBothATagAndItsChildIsListedOnce()
    {
        IReadOnlyList<TagNode> tags = Build(("a.md", "---\ntags: [llm, llm/agents]\n---\n\n# A\n"));

        TagNode llm = Assert.Single(tags);

        Assert.Equal(1, llm.TotalCount);
        Assert.Single(llm.AllDocuments);
    }

    [Fact]
    public void ASiblingThatMerelyStartsWithTheSameLettersIsNotAChild()
    {
        // "llm-ops" is its own tag, not something under "llm". The search index has to
        // draw the line in the same place, or the rail's count and the search's disagree.
        IReadOnlyList<TagNode> tags = Build(
            ("a.md", "---\ntags: [llm]\n---\n\n# A\n"),
            ("b.md", "---\ntags: [llm-ops]\n---\n\n# B\n"));

        Assert.Equal(2, tags.Count);
        Assert.Equal(1, tags.Single(t => t.FullTag == "llm").TotalCount);
    }

    [Fact]
    public void InlineTagsCountAsWellAsFrontmatterOnes()
    {
        IReadOnlyList<TagNode> tags = Build(("a.md", "# A\n\nAbout #llm/agents today.\n"));

        TagNode llm = Assert.Single(tags);

        Assert.Equal(1, llm.TotalCount);
        Assert.Equal("agents", Assert.Single(llm.Children).Segment);
    }

    private static IReadOnlyList<TagNode> Build(params (string Path, string Content)[] files) =>
        TagTree.Build(new VaultSnapshot
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(VaultFlavor.Generic),
            Index = VaultIndex.Build([.. files.Select(f => TestVault.CreateDocument(f.Path, f.Content))]),
        });
}
