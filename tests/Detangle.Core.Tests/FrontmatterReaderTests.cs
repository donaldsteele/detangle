using Detangle.Core.Parsing;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for the frontmatter key union in plan.md section 3.3.</summary>
public class FrontmatterReaderTests
{
    [Fact]
    public void ReadsYamlKeyUnion()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(
            """
            ---
            title: A Note
            aliases: [First, Second]
            aka: Third
            tags: "alpha, #beta"
            uid: note-1
            kind: concept
            state: draft
            related:
              - other-note
            author: Ada
            nav_order: 3
            ---

            # Body
            """);

        Assert.Equal(FrontmatterKind.Yaml, frontmatter.Kind);
        Assert.Equal("A Note", frontmatter.Title);
        Assert.Equal(["First", "Second", "Third"], frontmatter.Aliases);
        Assert.Equal(["alpha", "beta"], frontmatter.Tags);
        Assert.Equal("note-1", frontmatter.Id);
        Assert.Equal("concept", frontmatter.Type);
        Assert.Equal("draft", frontmatter.Status);
        Assert.Equal(["other-note"], frontmatter.References);
        Assert.Equal(["Ada"], frontmatter.Authors);
        Assert.Equal(3, frontmatter.Order);
    }

    [Fact]
    public void ToleratesBomAndLeadingBlankLines()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("﻿\n\n---\ntitle: Tolerated\n---\n");

        Assert.Equal("Tolerated", frontmatter.Title);
    }

    [Fact]
    public void ReportsUnterminatedBlockWithoutThrowing()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("---\ntitle: Broken\n\n# Body\n");

        Assert.Equal(FrontmatterKind.None, frontmatter.Kind);
        Assert.Single(frontmatter.Diagnostics);
    }

    [Fact]
    public void ReadsTomlDelimiters()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(
            "+++\ntitle = \"Hugo Page\"\nweight = 2\ntags = [\"a\", \"b\"]\n+++\n");

        Assert.Equal(FrontmatterKind.Toml, frontmatter.Kind);
        Assert.Equal("Hugo Page", frontmatter.Title);
        Assert.Equal(2, frontmatter.Order);
        Assert.Equal(["a", "b"], frontmatter.Tags);
    }

    [Fact]
    public void ReadsJsonDelimiters()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(
            ";;;\n{ \"title\": \"Json Page\", \"id\": \"j1\" }\n;;;\n");

        Assert.Equal(FrontmatterKind.Json, frontmatter.Kind);
        Assert.Equal("Json Page", frontmatter.Title);
        Assert.Equal("j1", frontmatter.Id);
    }

    [Fact]
    public void ReadsLogseqDoubleColonProperties()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(
            "title:: projects/detangle\nid:: 6512a0f1-1111-2222-3333-444455556666\n\n- A block.\n");

        Assert.Equal(FrontmatterKind.DoubleColon, frontmatter.Kind);
        Assert.Equal("projects/detangle", frontmatter.Title);
        Assert.Equal("6512a0f1-1111-2222-3333-444455556666", frontmatter.Id);
    }

    [Fact]
    public void ReadsDendronEpochMilliseconds()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("---\ncreated: 1745107800000\n---\n");

        Assert.Equal(2025, frontmatter.Created?.Year);
    }

    [Fact]
    public void ReadsEpochSeconds()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("---\ncreated: 1745107800\n---\n");

        Assert.Equal(2025, frontmatter.Created?.Year);
    }

    [Theory]
    [InlineData("draft: true", true)]
    [InlineData("draft: false", false)]
    [InlineData("publish: false", true)]
    [InlineData("publish: true", false)]
    public void FoldsInvertedDraftAndPublishPolarity(string line, bool expected)
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read($"---\n{line}\n---\n");

        Assert.Equal(expected, frontmatter.IsDraft);
    }

    [Fact]
    public void KeepsUnclaimedKeysForThePropertiesCard()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("---\ntitle: A\nconfidence: high\n---\n");

        Assert.Equal("high", frontmatter.Extra["confidence"]);
        Assert.False(frontmatter.Extra.ContainsKey("title"));
    }

    [Fact]
    public void FlattensNestedBlocksSuchAsLlmWikiGraph()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(
            "---\ngraph:\n  centrality: 0.8\n  cluster: attention\n---\n");

        Assert.Equal("centrality: 0.8, cluster: attention", frontmatter.Extra["graph"]);
    }

    [Fact]
    public void NoFrontmatterYieldsTheEmptyBlock()
    {
        DocumentFrontmatter frontmatter = FrontmatterReader.Read("# Just a heading\n");

        Assert.Equal(FrontmatterKind.None, frontmatter.Kind);
        Assert.Equal(0, frontmatter.LineCount);
    }
}
