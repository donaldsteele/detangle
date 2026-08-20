using Detangle.Core.Linking;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>Tests for the github-slugger reimplementation used by anchor resolution.</summary>
public class HeadingSluggerTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("Don't, Really?", "dont-really")]
    [InlineData("C# and F#", "c-and-f")]
    [InlineData("A & B", "a--b")]
    [InlineData("snake_case stays", "snake_case-stays")]
    [InlineData("already-hyphenated", "already-hyphenated")]
    [InlineData("  Padded  Heading  ", "padded--heading")]
    public void DeletesPunctuationRatherThanReplacingIt(string heading, string expected) =>
        Assert.Equal(expected, HeadingSlugger.SlugCore(heading));

    [Fact]
    public void AppendsCountersToDuplicateHeadings()
    {
        var slugger = new HeadingSlugger();

        Assert.Equal("duplicate", slugger.Slug("Duplicate"));
        Assert.Equal("duplicate-1", slugger.Slug("Duplicate"));
        Assert.Equal("duplicate-2", slugger.Slug("Duplicate"));
    }

    [Fact]
    public void ResetForgetsPreviousDocument()
    {
        var slugger = new HeadingSlugger();

        slugger.Slug("Duplicate");
        slugger.Reset();

        Assert.Equal("duplicate", slugger.Slug("Duplicate"));
    }
}
