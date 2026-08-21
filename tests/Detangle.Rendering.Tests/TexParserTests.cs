using Detangle.Rendering.Typesetting;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the TeX subset the reader typesets. The corpus is the notation that turns
/// up in wikis a language model wrote about machine learning, because that is what this
/// parser exists to set.
/// </summary>
public class TexParserTests
{
    [Fact]
    public void ALetterIsAVariableAndADigitIsNot()
    {
        Assert.Equal(new MathAtom("x", MathStyle.Variable), TexParser.Parse("x"));
        Assert.Equal(new MathAtom("42", MathStyle.Upright), TexParser.Parse("42"));
    }

    [Fact]
    public void DigitsRunTogetherIntoOneNumber()
    {
        // "128" has to be one atom, or a script would attach to the 8 alone.
        Assert.Equal(new MathAtom("128", MathStyle.Upright), TexParser.Parse("128"));
        Assert.Equal(new MathAtom("0.5", MathStyle.Upright), TexParser.Parse("0.5"));
    }

    [Fact]
    public void GreekResolvesToItsLetter()
    {
        Assert.Equal(new MathAtom("π", MathStyle.Variable), TexParser.Parse(@"\pi"));
        Assert.Equal(new MathAtom("Σ", MathStyle.Upright), TexParser.Parse(@"\Sigma"));
    }

    [Fact]
    public void AFractionKeepsBothHalves()
    {
        var fraction = Assert.IsType<MathFraction>(TexParser.Parse(@"\frac{a}{b}"));

        Assert.Equal(new MathAtom("a", MathStyle.Variable), fraction.Numerator);
        Assert.Equal(new MathAtom("b", MathStyle.Variable), fraction.Denominator);
    }

    [Fact]
    public void AFractionTakesSingleTokensWithoutBraces()
    {
        var fraction = Assert.IsType<MathFraction>(TexParser.Parse(@"\frac12"));

        Assert.Equal(new MathAtom("1", MathStyle.Upright), fraction.Numerator);
        Assert.Equal(new MathAtom("2", MathStyle.Upright), fraction.Denominator);
    }

    [Fact]
    public void ARadicalCarriesItsIndex()
    {
        var plain = Assert.IsType<MathRadical>(TexParser.Parse(@"\sqrt{2}"));

        Assert.Null(plain.Index);

        var cube = Assert.IsType<MathRadical>(TexParser.Parse(@"\sqrt[3]{x}"));

        Assert.Equal(new MathAtom("3", MathStyle.Upright), cube.Index);
    }

    [Fact]
    public void ScriptsAttachToWhatPrecedesThem()
    {
        var scripts = Assert.IsType<MathScripts>(TexParser.Parse("x^2"));

        Assert.Equal(new MathAtom("x", MathStyle.Variable), scripts.Nucleus);
        Assert.Equal(new MathAtom("2", MathStyle.Upright), scripts.Superscript);
        Assert.Null(scripts.Subscript);
    }

    [Fact]
    public void BothScriptsCanAppearInEitherOrder()
    {
        var first = Assert.IsType<MathScripts>(TexParser.Parse("x_i^2"));
        var second = Assert.IsType<MathScripts>(TexParser.Parse("x^2_i"));

        Assert.Equal(new MathAtom("2", MathStyle.Upright), first.Superscript);
        Assert.Equal(new MathAtom("i", MathStyle.Variable), first.Subscript);
        Assert.Equal(first.Superscript, second.Superscript);
        Assert.Equal(first.Subscript, second.Subscript);
    }

    [Fact]
    public void AGroupedScriptStaysWhole()
    {
        var scripts = Assert.IsType<MathScripts>(TexParser.Parse("e^{i\\pi}"));
        var row = Assert.IsType<MathRow>(scripts.Superscript);

        Assert.Equal(2, row.Children.Count);
        Assert.Equal(new MathAtom("π", MathStyle.Variable), row.Children[1]);
    }

    [Fact]
    public void TextIsKeptVerbatimAndUpright()
    {
        // Parsing this as math would italicise it and swallow the space.
        Assert.Equal(
            new MathAtom("is all you need", MathStyle.Text),
            TexParser.Parse(@"\text{is all you need}"));
    }

    [Fact]
    public void GrowingDelimitersAreRecorded()
    {
        var fenced = Assert.IsType<MathFenced>(TexParser.Parse(@"\left(\frac{a}{b}\right)"));

        Assert.Equal("(", fenced.Open);
        Assert.Equal(")", fenced.Close);
        Assert.IsType<MathFraction>(fenced.Body);
    }

    [Fact]
    public void ADroppedDelimiterIsAllowed()
    {
        var fenced = Assert.IsType<MathFenced>(TexParser.Parse(@"\left.\frac{a}{b}\right|"));

        Assert.Empty(fenced.Open);
        Assert.Equal("|", fenced.Close);
    }

    [Fact]
    public void SpacingCommandsBecomeSpace()
    {
        var row = Assert.IsType<MathRow>(TexParser.Parse(@"a\,b"));

        Assert.Contains(row.Children, child => child is MathSpace);
    }

    [Fact]
    public void AnUnknownCommandIsPreservedRatherThanGuessedAt()
    {
        var row = Assert.IsType<MathRow>(TexParser.Parse(@"x \notacommand y"));

        Assert.Contains(row.Children, child => child is MathUnknown { Source: @"\notacommand" });
    }

    [Fact]
    public void TheAttentionEquationParsesWholly()
    {
        // The equation on the demo wiki's paper page, and the reason this parser exists.
        MathNode node = TexParser.Parse(
            @"\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^\top}{\sqrt{d_k}}\right)V");

        var row = Assert.IsType<MathRow>(node);

        Assert.Contains(Flatten(row), n => n is MathAtom { Text: "Attention", Style: MathStyle.Text });
        Assert.Contains(Flatten(row), n => n is MathFenced { Open: "(", Close: ")" });
        Assert.Contains(Flatten(row), n => n is MathFraction);
        Assert.Contains(Flatten(row), n => n is MathRadical);
        Assert.Contains(Flatten(row), n => n is MathAtom { Text: "⊤" });

        // And nothing in it was left unrecognised.
        Assert.DoesNotContain(Flatten(row), n => n is MathUnknown);
    }

    [Theory]
    [InlineData(@"E = mc^2")]
    [InlineData(@"e^{i\pi} + 1 = 0")]
    [InlineData(@"O(n^2 d)")]
    [InlineData(@"\sum_{i=1}^{n} x_i")]
    [InlineData(@"\int_0^\infty e^{-x} dx")]
    [InlineData(@"\frac{\partial L}{\partial \theta}")]
    [InlineData(@"\sqrt{d_k}")]
    [InlineData(@"\alpha \leq \beta \neq \gamma")]
    public void TheCommonFormsParseWithoutLeavingAnythingUnknown(string source)
    {
        Assert.DoesNotContain(Flatten(TexParser.Parse(source)), n => n is MathUnknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData(@"\frac{")]
    [InlineData(@"\sqrt")]
    [InlineData(@"x^")]
    [InlineData(@"\left(")]
    [InlineData(@"{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{{x}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}}")]
    public void MalformedSourceIsParsedRatherThanThrown(string source)
    {
        // A wiki will contain broken math, and an exception in the renderer would take
        // the whole page with it.
        Assert.NotNull(TexParser.Parse(source));
    }

    private static IEnumerable<MathNode> Flatten(MathNode node)
    {
        yield return node;

        IEnumerable<MathNode> children = node switch
        {
            MathRow row => row.Children,
            MathFraction fraction => [fraction.Numerator, fraction.Denominator],
            MathRadical radical => radical.Index is null ? [radical.Radicand] : [radical.Radicand, radical.Index],
            MathScripts scripts =>
                new[] { scripts.Nucleus, scripts.Superscript, scripts.Subscript }.OfType<MathNode>(),
            MathFenced fenced => [fenced.Body],
            _ => [],
        };

        foreach (MathNode child in children)
        {
            foreach (MathNode descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
