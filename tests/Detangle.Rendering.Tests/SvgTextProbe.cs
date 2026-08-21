using System.Text.RegularExpressions;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;
using SkiaSharp;
using Svg.Skia;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Characterises how the SVG renderer handles text, and pins an open defect.
/// <para>
/// Diagram labels do not draw. The SVG is correct — real text content, a resolved font
/// size, a literal fill — and this renderer draws text perfectly well for the synthetic
/// documents below, including a family supplied through a style block. It draws none for
/// Mermaider's output, and removing every label from that document changes not one pixel.
/// </para>
/// <para>
/// Ruled out so far: unresolved CSS variables (fixed, and the colours and sizes are now
/// literal), a font size carrying a CSS unit, a font family only reachable through CSS,
/// numeric font weights, text anchoring with a dy shift, and grouping. Injecting a known
/// family straight onto the text elements does not help either, and neither does deleting
/// the style block outright.
/// </para>
/// <para>
/// The regression test below is skipped rather than deleted: it is the check that will
/// pass when this is fixed, and a green suite should not imply the diagrams are legible.
/// </para>
/// </summary>
public partial class SvgTextProbe
{
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><text x='10' y='30' font-size='20' fill='#ffffff'>Hello</text></svg>", "bare text, no family")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><text x='10' y='30' font-size='20' font-family='sans-serif' fill='#ffffff'>Hello</text></svg>", "family as an attribute")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><text x='10' y='30' font-size='20' font-family='Arial' fill='#ffffff'>Hello</text></svg>", "a real installed family")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><style>text{font-family:Arial;}</style><text x='10' y='30' font-size='20' fill='#ffffff'>Hello</text></svg>", "family via a style block")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><text x='10' y='30' font-size='20' font-weight='500' fill='#ffffff'>Hello</text></svg>", "numeric font weight")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><text x='10' y='30' font-size='20' text-anchor='middle' dy='5.6' fill='#ffffff'>Hello</text></svg>", "anchored with a dy shift")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 60' width='200' height='60'><g><text x='10' y='30' font-size='20' fill='#ffffff'>Hello</text></g></svg>", "inside a group")]
    public void WhichFormOfTextDoesTheRendererDraw(string svg, string description)
    {
        long inked = InkedPixels(svg);

        Assert.True(inked > 0, $"{description}: nothing was drawn");
    }

    [Fact(Skip = "Open defect: Svg.Skia draws no text for Mermaider's output. See the class comment.")]
    public void TheRendererDrawsTheLabels()
    {
        DiagramResult diagram = new MermaiderDiagramRenderer()
            .Render(DiagramKind.Mermaid, "graph LR\n  A[Input] --> B[Output]\n", DiagramTheme.Dark);

        Assert.Contains("Input", diagram.Svg, StringComparison.Ordinal);

        long withText = InkedPixels(diagram.Svg);
        long withoutText = InkedPixels(TextElement().Replace(diagram.Svg, string.Empty));

        // If removing every label changes nothing, the renderer was never drawing them.
        Assert.True(
            withText > withoutText,
            $"text contributed no pixels: {withText} with labels, {withoutText} without");
    }

    private static long InkedPixels(string svg)
    {
        using var picture = new SKSvg();

        Assert.NotNull(picture.FromSvg(svg));

        using var bitmap = new SKBitmap(600, 400);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);
        canvas.DrawPicture(picture.Picture);

        long inked = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    inked++;
                }
            }
        }

        return inked;
    }

    [GeneratedRegex("<text.*?</text>", RegexOptions.Singleline)]
    private static partial Regex TextElement();

    [GeneratedRegex("<style.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();
}
