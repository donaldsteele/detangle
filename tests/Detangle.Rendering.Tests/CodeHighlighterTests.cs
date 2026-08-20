using Detangle.Rendering.Highlighting;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>Tests for the TextMate-backed fence highlighter.</summary>
public class CodeHighlighterTests
{
    private static readonly CodeHighlighter Highlighter = new(HighlightTheme.Dark);

    [Theory]
    [InlineData("csharp")]
    [InlineData("json")]
    [InlineData("python")]
    public void HasGrammarsForCommonLanguages(string language) =>
        Assert.True(Highlighter.CanHighlight(language));

    [Fact]
    public void SplitsCodeIntoColouredSpans()
    {
        IReadOnlyList<IReadOnlyList<HighlightSpan>> lines =
            Highlighter.Highlight("csharp", "var x = 1;\n");

        IReadOnlyList<HighlightSpan> first = lines[0];

        Assert.True(first.Count > 1);
        Assert.Contains(first, span => span.Foreground is not null);
        Assert.Equal("var x = 1;", string.Concat(first.Select(s => s.Text)));
    }

    [Fact]
    public void KeepsStateAcrossLinesSoMultiLineConstructsStayColoured()
    {
        IReadOnlyList<IReadOnlyList<HighlightSpan>> lines =
            Highlighter.Highlight("csharp", "/* a comment\nstill a comment */\nvar x = 1;");

        // If the tokenizer's state were reset per line, line two would tokenize as code.
        Assert.Equal(
            lines[0].First(s => s.Foreground is not null).Foreground,
            lines[1].First(s => s.Foreground is not null).Foreground);
    }

    [Fact]
    public void UnknownLanguagesComeBackReadableRatherThanEmpty()
    {
        IReadOnlyList<IReadOnlyList<HighlightSpan>> lines =
            Highlighter.Highlight("no-such-language", "some text\nmore text");

        Assert.Equal(["some text", "more text"], lines.Select(l => string.Concat(l.Select(s => s.Text))));
        Assert.All(lines, line => Assert.Null(Assert.Single(line).Foreground));
    }

    [Fact]
    public void UnlabelledFencesAreLeftAlone()
    {
        Assert.False(Highlighter.CanHighlight(string.Empty));
        Assert.Equal("plain", Assert.Single(Highlighter.Highlight(string.Empty, "plain")[0]).Text);
    }

    [Fact]
    public void PreservesEveryLineIncludingBlankOnes()
    {
        IReadOnlyList<IReadOnlyList<HighlightSpan>> lines =
            Highlighter.Highlight("csharp", "var a = 1;\n\nvar b = 2;");

        Assert.Equal(3, lines.Count);
    }
}
