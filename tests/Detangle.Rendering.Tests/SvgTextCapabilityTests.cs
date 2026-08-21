using Detangle.Rendering.Diagrams;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the diagram text remedy.
/// <para>
/// The failure it exists for is a platform where naming a font family at all makes every
/// glyph of a label draw at the same position, so a word becomes one smudge. It cannot be
/// reproduced on a desktop, so what is checked here is that this platform is diagnosed as
/// healthy, and that the remedy does exactly what it claims. The browser half of that —
/// which deliveries collapse, at which sizes and weights — is <see cref="SvgTextSelfTest"/>,
/// run against the published demo by tools/wasm-selftest.py.
/// </para>
/// </summary>
public class SvgTextCapabilityTests
{
    [Fact]
    public void ThisPlatformDrawsStyledTextCorrectly()
    {
        // A desktop failing this would mean the remedy is about to switch itself on here,
        // which deserves investigating rather than accepting.
        Assert.True(SvgTextCapability.CanDrawText, SvgTextCapability.Diagnosis);
        Assert.Contains("span", SvgTextCapability.Diagnosis, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRemedyRemovesEveryFontFamilyAndNothingElse()
    {
        const string Svg =
            "<svg><style>text { font-family: 'Inter', sans-serif; fill: red; } .mono "
            + "{ font-family: monospace; }</style><text x=\"1\" font-size=\"13\">Hi</text></svg>";

        string stripped = SvgStyleFlattener.RemoveFontFamilies(Svg);

        Assert.DoesNotContain("font-family", stripped, StringComparison.Ordinal);

        // Everything that positions or colours the label has to survive.
        Assert.Contains("font-size=\"13\"", stripped, StringComparison.Ordinal);
        Assert.Contains("fill: red", stripped, StringComparison.Ordinal);
        Assert.Contains(">Hi</text>", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRemedyLeavesSvgWithoutFamiliesUntouched()
    {
        const string Svg = "<svg><text x=\"1\" font-size=\"13\">Hi</text></svg>";

        Assert.Equal(Svg, SvgStyleFlattener.RemoveFontFamilies(Svg));
    }

    [Fact]
    public void TheRemedyCoversThePresentationAttributeToo()
    {
        // No diagram Detangle renders reaches this branch today — Mermaider emits
        // declarations, measured on its output for "graph LR": two declarations, zero
        // attributes. It is covered because the browser matrix scores the attribute rows
        // 1.00 to 1.33 exactly like the declaration rows, so a generator that changed its
        // mind about the spelling would silently bring the smudge back.
        //
        // What is NOT worth doing is the reverse, rewriting declarations INTO attributes
        // to keep the author's typeface: the browser draws both spellings identically, and
        // Svg's parser flattens the cascade into Attributes["font-family"] before the
        // renderer ever sees it, so the rewrite would change nothing at all.
        const string Svg =
            "<svg><text x=\"1\" font-family=\"sans-serif\" font-size=\"13\">Hi</text>"
            + "<text font-family='Inter'>There</text></svg>";

        string stripped = SvgStyleFlattener.RemoveFontFamilies(Svg);

        Assert.DoesNotContain("font-family", stripped, StringComparison.Ordinal);
        Assert.Contains("font-size=\"13\"", stripped, StringComparison.Ordinal);
        Assert.Contains(">Hi</text>", stripped, StringComparison.Ordinal);
        Assert.Contains(">There</text>", stripped, StringComparison.Ordinal);
    }
}
