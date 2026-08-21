using Detangle.Rendering.Diagrams;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the diagram text remedy.
/// <para>
/// The failure it exists for is a platform where naming a font family through a CSS rule
/// makes every glyph of a label draw at the same position, so a word becomes one smudge.
/// It cannot be reproduced on a desktop, so what is checked here is that this platform is
/// diagnosed as healthy, and that the remedy does exactly what it claims.
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
}
