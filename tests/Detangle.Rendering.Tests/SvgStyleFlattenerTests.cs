using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the CSS flattening pass that makes Mermaider's SVG drawable.
/// <para>
/// The bug these exist for: Mermaider writes font sizes as <c>var(--fs-m)</c>, expecting a
/// browser to resolve them. Nothing did, so every label in every diagram drew at no size —
/// the shapes appeared and the words did not, and the diagram tests never noticed because
/// they only asserted that an SVG came back.
/// </para>
/// </summary>
public class SvgStyleFlattenerTests
{
    [Fact]
    public void ADiagramKeepsNoUnresolvedVariables()
    {
        DiagramResult result = Render("graph LR\n  A[Input] --> B[Output]\n");

        Assert.NotEmpty(result.Svg);
        Assert.DoesNotContain("var(--", result.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("color-mix(", result.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelsGetARealFontSize()
    {
        DiagramResult result = Render("graph LR\n  A[Input] --> B[Output]\n");

        // The whole defect in one assertion: a text element with a resolvable size.
        Assert.Contains("<text", result.Svg, StringComparison.Ordinal);
        // Unitless: a presentation attribute takes user units, and "13px" is not a number.
        Assert.Contains("font-size=\"13\"", result.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLabelTextItselfSurvives()
    {
        DiagramResult result = Render("graph LR\n  A[Input] --> B[Output]\n");

        Assert.Contains("Input", result.Svg, StringComparison.Ordinal);
        Assert.Contains("Output", result.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThemeReachesTheDrawing()
    {
        DiagramResult dark = Render("graph LR\n  A --> B\n", DiagramTheme.Dark);
        DiagramResult light = Render("graph LR\n  A --> B\n", DiagramTheme.Light);

        Assert.Contains("#e7eaf0", dark.Svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#12151b", light.Svg, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("fill=\"var(--fg)\"", "fill=\"#111111\"")]
    [InlineData("fill=\"var(--missing, #abcdef)\"", "fill=\"#abcdef\"")]
    [InlineData("font-size=\"var(--fs-m)\"", "font-size=\"13\"")]
    public void VariablesResolveIncludingFallbacks(string input, string expected)
    {
        string svg = SvgStyleFlattener.Flatten($"<svg>{input}</svg>", Seed);

        Assert.Contains(expected, svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentDeclarationIsUsedWhenTheSeedHasNothingToSay()
    {
        string svg = SvgStyleFlattener.Flatten(
            "<svg><style>svg { --local: #123456; }</style><rect fill=\"var(--local)\" /></svg>", Seed);

        Assert.Contains("fill=\"#123456\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSeedBeatsTheDocumentsOwnDefault()
    {
        // The document's value is what it would use without a host; this application is
        // the host, and its palette wins.
        string svg = SvgStyleFlattener.Flatten(
            "<svg><style>svg { --fg: #000000; }</style><rect fill=\"var(--fg)\" /></svg>", Seed);

        Assert.Contains("fill=\"#111111\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ChainedDefinitionsResolveThroughToALiteral()
    {
        string svg = SvgStyleFlattener.Flatten(
            "<svg><style>svg { --a: var(--fg); --b: var(--a); }</style><rect fill=\"var(--b)\" /></svg>",
            Seed);

        Assert.Contains("fill=\"#111111\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ColourMixBlendsInSrgb()
    {
        string svg = SvgStyleFlattener.Flatten(
            "<svg><rect fill=\"color-mix(in srgb, #000000 50%, #ffffff)\" /></svg>", Seed);

        Assert.Contains("fill=\"#808080\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ColourMixNestedInsideAFallbackStillResolves()
    {
        // The shape Mermaider actually emits.
        string svg = SvgStyleFlattener.Flatten(
            "<svg><rect fill=\"var(--muted, color-mix(in srgb, var(--fg) 20%, var(--bg)))\" /></svg>",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["--fg"] = "#000000",
                ["--bg"] = "#ffffff",
            });

        Assert.DoesNotContain("var(", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("color-mix", svg, StringComparison.Ordinal);
        Assert.Contains("#cccccc", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SvgWithoutVariablesIsReturnedUntouched()
    {
        const string Plain = "<svg><rect fill=\"#ff0000\" /></svg>";

        Assert.Same(Plain, SvgStyleFlattener.Flatten(Plain, Seed));
    }

    [Fact]
    public void ACircularDefinitionTerminates()
    {
        // Nonsense input must not hang the renderer that draws every page.
        string svg = SvgStyleFlattener.Flatten(
            "<svg><style>svg { --a: var(--b); --b: var(--a); }</style><rect fill=\"var(--a)\" /></svg>",
            Seed);

        Assert.NotNull(svg);
    }

    private static readonly Dictionary<string, string> Seed = new(StringComparer.Ordinal)
    {
        ["--fg"] = "#111111",
        ["--bg"] = "#ffffff",
        ["--fs-m"] = "13",
    };

    private static DiagramResult Render(string source, DiagramTheme theme = DiagramTheme.Dark) =>
        new MermaiderDiagramRenderer().Render(DiagramKind.Mermaid, source, theme);
}
