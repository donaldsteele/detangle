using System.Text.RegularExpressions;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;
using SkiaSharp;
using Svg.Skia;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Characterises how the SVG renderer handles text, and guards that diagram labels draw.
/// <para>
/// This file once recorded an open defect — "Mermaider's labels never draw, and removing
/// every one of them changes not a pixel". The defect was in the instrument. Diagram
/// labels sit inside opaque node fills, so counting ink against a cleared background
/// cannot see them: a glyph painted over a filled rectangle changes what colour a pixel
/// is, never whether it is coloured at all. The count could not have moved whatever the
/// renderer did, so it "proved" the labels missing when they are in fact drawn, legible
/// and correctly centred.
/// </para>
/// <para>
/// The honest measure is a with/without raster difference, which sees ink of any colour
/// anywhere, including on top of an existing fill. That is strictly harder to satisfy
/// than the old count, and still fails if the renderer ever stops drawing text.
/// </para>
/// <para>
/// The theory below keeps the ink count deliberately: its text lands on bare background,
/// where the count is valid, and it characterises which forms of text resolve a typeface
/// at all.
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

    [Fact]
    public void TheRendererDrawsTheLabels()
    {
        DiagramResult diagram = new MermaiderDiagramRenderer()
            .Render(DiagramKind.Mermaid, "graph LR\n  A[Input] --> B[Output]\n", DiagramTheme.Dark);

        Assert.Contains("Input", diagram.Svg, StringComparison.Ordinal);

        using SKBitmap withText = Render(diagram.Svg);
        using SKBitmap withoutText = Render(TextElement().Replace(diagram.Svg, string.Empty));

        long changed = ChangedPixels(withText, withoutText);

        // If removing every label changes nothing, the renderer was never drawing them.
        Assert.True(
            changed > 0,
            "removing every label changed no pixel: the labels were never drawn");
    }

    private static SKBitmap Render(string svg)
    {
        using var picture = new SKSvg();

        Assert.NotNull(picture.FromSvg(svg));

        // The caller owns the bitmap: it has to outlive the canvas that painted into it.
        SKBitmap bitmap = new(600, 400);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Black);
        canvas.DrawPicture(picture.Picture);

        return bitmap;
    }

    private static long ChangedPixels(SKBitmap left, SKBitmap right)
    {
        long changed = 0;

        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    private static long InkedPixels(string svg)
    {
        using SKBitmap bitmap = Render(svg);

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
}
