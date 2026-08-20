using Detangle.Core.Linking;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for N(s), the normalization function in plan.md section 5.2.</summary>
public class LinkNormalizerTests
{
    [Theory]
    [InlineData("Attention Is All You Need", "attention-is-all-you-need")]
    [InlineData("attention_is_all_you_need", "attention-is-all-you-need")]
    [InlineData("attention.is.all.you.need", "attention-is-all-you-need")]
    [InlineData("Attention   Is  All", "attention-is-all")]
    [InlineData("  padded  ", "padded")]
    [InlineData("--leading-and-trailing--", "leading-and-trailing")]
    public void CollapsesSeparatorsAndCase(string input, string expected) =>
        Assert.Equal(expected, LinkNormalizer.Normalize(input));

    [Theory]
    [InlineData("note.md", "note")]
    [InlineData("note.markdown", "note")]
    [InlineData("note.mdx", "note")]
    [InlineData("note.html", "note")]
    public void StripsKnownExtensionsBeforeCollapsingDots(string input, string expected) =>
        Assert.Equal(expected, LinkNormalizer.Normalize(input));

    [Fact]
    public void KeepsUnknownExtensionsAsPartOfTheName()
    {
        // ".png" is not a document extension, so "diagram.png" is a filename, not a stem
        // that happens to end in a dot segment.
        Assert.Equal("diagram-png", LinkNormalizer.Normalize("diagram.png"));
    }

    [Theory]
    [InlineData("encoded%20target", "encoded-target")]
    [InlineData("encoded%2520target", "encoded-target")]
    [InlineData("a%2Fb", "a/b")]
    public void DecodesPercentEscapesUpToTwiceOver(string input, string expected) =>
        Assert.Equal(expected, LinkNormalizer.Normalize(input));

    [Fact]
    public void StopsDecodingAfterTwoRounds()
    {
        // Triple-encoded input is far likelier to hold a literal percent than to be a
        // third round of escaping, so the last round is left alone.
        Assert.Equal("a%20b", LinkNormalizer.DecodePercentEscapes("a%252520b"));
    }

    [Fact]
    public void ComposesUnicodeToNfc()
    {
        string decomposed = "café";
        string composed = "café";

        Assert.Equal(LinkNormalizer.Normalize(composed), LinkNormalizer.Normalize(decomposed));
    }

    [Fact]
    public void NormalizePathKeepsSlashesAndRelativeSegments()
    {
        Assert.Equal("../a-folder/a-note", LinkNormalizer.NormalizePath("../A Folder/A_Note.md"));
    }

    [Fact]
    public void NormalizePathAcceptsWindowsSeparators()
    {
        Assert.Equal("notes/sub/page", LinkNormalizer.NormalizePath(@"notes\sub\page.md"));
    }

    [Theory]
    [InlineData("note.md", true)]
    [InlineData("note.mdx", true)]
    [InlineData("diagram.png", false)]
    [InlineData("lang.python.basics", false)]
    public void RecognisesMarkdownExtensions(string input, bool expected) =>
        Assert.Equal(expected, LinkNormalizer.HasMarkdownExtension(input));
}
