using Detangle.Rendering.Diagrams;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Guards the diagnostic that makes the WebAssembly text defect reproducible.
/// <para>
/// The defect itself cannot be reproduced on a desktop, so what is checked here is that
/// the instrument works: that it discriminates — a stacked word must score differently
/// from an advancing one — and that this platform, where every variant is known good,
/// comes back clean. An instrument that answers "advancing" whatever it is shown is worth
/// nothing, and that is exactly the failure mode that let the original bug survive.
/// </para>
/// </summary>
public class SvgTextSelfTestTests
{
    [Fact]
    public void EveryVariantDrawsAdvancingGlyphsOnThisPlatform()
    {
        IReadOnlyList<SvgTextSelfTest.Sample> samples = SvgTextSelfTest.Run();

        // Four deliveries, two sizes, two weights.
        Assert.Equal(16, samples.Count);

        foreach (SvgTextSelfTest.Sample sample in samples)
        {
            Assert.True(
                sample.Advancing,
                $"{sample.Delivery} family, {sample.FontSize}px, weight {sample.Weight}: "
                + $"MMMM spans {sample.RepeatedSpan}px against M's {sample.SingleSpan}px "
                + $"(ratio {sample.Ratio:0.00}) — {sample.Verdict}");
        }
    }

    [Fact]
    public void TheMatrixCoversTheDeliveryThatBreaks()
    {
        IReadOnlyList<SvgTextSelfTest.Sample> samples = SvgTextSelfTest.Run();

        // The whole point of the matrix is the contrast between a family that arrives
        // through CSS — the delivery that collapses on WebAssembly — and one that does not.
        Assert.Contains(samples, sample => sample.Delivery == "css");
        Assert.Contains(samples, sample => sample.Delivery == "attribute");
        Assert.Contains(samples, sample => sample.Delivery == "style-attr");
        Assert.Contains(samples, sample => sample.Delivery == "none");
    }

    [Fact]
    public void AStackedWordScoresAsCollapsed()
    {
        // The instrument has to be shown failing something, or a clean run proves nothing.
        // Four letters drawn at one x is precisely what WebAssembly does, so it is written
        // out by hand here and measured with the same code the matrix uses.
        IReadOnlyList<SvgTextSelfTest.Sample> samples = SvgTextSelfTest.Run();

        SvgTextSelfTest.Sample healthy = samples.First(
            sample => sample is { Delivery: "css", FontSize: 24, Weight: "400" });

        Assert.True(healthy.Ratio > 3, $"four advancing letters should span about four widths, not {healthy.Ratio:0.00}");

        // The same four letters, each element anchored at the same x: a word with no
        // advance. It must land near a ratio of one, well under the threshold.
        double stacked = StackedRatio();

        Assert.True(stacked < 2, $"a stacked word should score about 1.00, not {stacked:0.00}");
    }

    /// <summary>
    /// Measures a hand-built stacked word the way the browser defect draws one: every
    /// glyph in its own element, all at the same x.
    /// </summary>
    private static double StackedRatio()
    {
        const string One =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"48\">"
            + "<text x=\"2\" y=\"36\" font-size=\"24\" fill=\"#ffffff\">M</text></svg>";

        const string Stacked =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"48\">"
            + "<text x=\"2\" y=\"36\" font-size=\"24\" fill=\"#ffffff\">M</text>"
            + "<text x=\"2\" y=\"36\" font-size=\"24\" fill=\"#ffffff\">M</text>"
            + "<text x=\"2\" y=\"36\" font-size=\"24\" fill=\"#ffffff\">M</text>"
            + "<text x=\"2\" y=\"36\" font-size=\"24\" fill=\"#ffffff\">M</text></svg>";

        return (double)SpanOf(Stacked) / SpanOf(One);
    }

    private static int SpanOf(string svg)
    {
        SvgInkSpan.Reading reading = SvgInkSpan.Measure(svg, 320, 48);

        Assert.True(reading.Parsed, "the probe document did not parse");
        Assert.True(reading.First >= 0, "the probe document drew no ink");

        return reading.Span;
    }
}
