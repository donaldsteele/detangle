using SkiaSharp;
using Svg.Skia;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Rasterises an SVG and reports the horizontal extent of its ink.
/// <para>
/// This is the one measurement in the codebase that has ever told the truth about the
/// WebAssembly text defect. Counting inked pixels cannot see it — a diagram label sits on
/// top of an opaque node fill, so painting glyphs changes what colour a pixel is and never
/// whether it is coloured at all, and a count "proved" labels missing that were in fact
/// drawn. Asking the font manager cannot see it either: WebAssembly resolves
/// <c>sans-serif</c> to a real face with 897 glyphs and measures a nine-letter word at 72
/// pixels, then draws all nine letters on top of each other.
/// </para>
/// <para>
/// Where the glyphs landed is the only question that survives all of that, so this
/// measures exactly that and nothing else. Both <see cref="SvgTextCapability"/> and
/// <see cref="SvgTextSelfTest"/> go through here, so the gate and the diagnostic can never
/// disagree about what they saw.
/// </para>
/// </summary>
internal static class SvgInkSpan
{
    /// <summary>What one rasterised probe document showed.</summary>
    /// <param name="Parsed">False when the SVG did not parse at all.</param>
    /// <param name="First">Leftmost inked column, or -1 when nothing was drawn.</param>
    /// <param name="Last">Rightmost inked column, or -1 when nothing was drawn.</param>
    internal readonly record struct Reading(bool Parsed, int First, int Last)
    {
        /// <summary>Columns between the first and last ink, or -1 when there was none.</summary>
        internal int Span => First < 0 ? -1 : Last - First;
    }

    /// <summary>
    /// Draws <paramref name="svg"/> in white on black and reports where the ink fell.
    /// </summary>
    /// <param name="svg">The probe document. It must paint in a light colour.</param>
    /// <param name="width">Raster width, matching the document's.</param>
    /// <param name="height">Raster height, matching the document's.</param>
    internal static Reading Measure(string svg, int width, int height) => Measure(svg, width, height, settings: null);

    /// <summary>
    /// Draws <paramref name="svg"/> through a renderer configured by
    /// <paramref name="settings"/> and reports where the ink fell.
    /// </summary>
    /// <param name="svg">The probe document. It must paint in a light colour.</param>
    /// <param name="width">Raster width, matching the document's.</param>
    /// <param name="height">Raster height, matching the document's.</param>
    /// <param name="settings">
    /// Applied to the renderer before the document is read, or null for its defaults. This
    /// is how a typeface provider gets in front of the font lookup, which is the one
    /// variable worth changing about how an SVG is drawn.
    /// </param>
    internal static Reading Measure(string svg, int width, int height, Action<SKSvgSettings>? settings)
    {
        using var picture = new SKSvg();

        settings?.Invoke(picture.Settings);

        if (picture.FromSvg(svg) is null || picture.Picture is null)
        {
            return new Reading(false, -1, -1);
        }

        return Measure(canvas => canvas.DrawPicture(picture.Picture), width, height);
    }

    /// <summary>
    /// Measures whatever <paramref name="draw"/> paints, on the same black ground and by
    /// the same rule as the SVG overload.
    /// <para>
    /// Text that is drawn straight onto a canvas never passes through Svg.Skia, so putting
    /// both through this is what separates "this platform cannot advance glyphs" from
    /// "the SVG renderer cannot".
    /// </para>
    /// </summary>
    internal static Reading Measure(Action<SKCanvas> draw, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            draw(canvas);
        }

        (int first, int last) = InkedColumns(bitmap);

        return new Reading(true, first, last);
    }

    private static (int First, int Last) InkedColumns(SKBitmap bitmap)
    {
        int first = -1;
        int last = -1;

        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).Red <= 100)
                {
                    continue;
                }

                first = first < 0 ? x : first;
                last = x;

                break;
            }
        }

        return (first, last);
    }
}
