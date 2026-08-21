using SkiaSharp;
using Svg.Skia;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Asks the platform, once, whether it can draw SVG text correctly.
/// <para>
/// Every cheaper question gave the wrong answer. WebAssembly reports a font family,
/// resolves "sans-serif" to a real face with 897 glyphs, and measures a nine-letter word
/// at 72 pixels — and then draws all the letters of a label on top of each other. Bisecting
/// it there showed the trigger precisely: text draws correctly at any size, weight and
/// anchor, and collapses only once a font-family reaches it through a CSS rule.
/// </para>
/// <para>
/// So the probe renders that exact shape — two letters styled through a style block — and
/// checks the second one landed to the right of the first.
/// </para>
/// </summary>
public static class SvgTextCapability
{
    private const int Width = 64;
    private const int Height = 32;

    private static readonly Lock Gate = new();

    private static bool? _canDrawText;

    /// <summary>
    /// True when SVG text draws with advancing glyphs. Measured once and remembered; the
    /// answer cannot change while the process runs.
    /// </summary>
    public static bool CanDrawText
    {
        get
        {
            if (_canDrawText is { } known)
            {
                return known;
            }

            lock (Gate)
            {
                _canDrawText ??= Measure();

                return _canDrawText.Value;
            }
        }
    }

    /// <summary>What the probe saw, for diagnosis where no debugger can be attached.</summary>
    public static string Diagnosis { get; private set; } = "not yet measured";

    private static bool Measure()
    {
        try
        {
            // Two wide letters far apart in the alphabet, so a correct rendering puts ink
            // in two clearly separated columns and a broken one puts it all in one.
            // The style block is the whole point of the probe. Text with no font-family
            // draws correctly everywhere; text whose family arrives through CSS is what
            // collapses, so that is the document that has to be measured.
            const string Probe =
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"32\">"
                + "<style>text { font-family: sans-serif; }</style>"
                + "<text x=\"2\" y=\"24\" font-size=\"24\" fill=\"#ffffff\">MM</text></svg>";

            using var svg = new SKSvg();

            if (svg.FromSvg(Probe) is null || svg.Picture is null)
            {
                Diagnosis = "the probe did not parse";

                return false;
            }

            using var bitmap = new SKBitmap(Width, Height);

            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawPicture(svg.Picture);
            }

            (int first, int last) = InkedColumns(bitmap);

            if (first < 0)
            {
                Diagnosis = "no text was drawn at all";

                return false;
            }

            // Two 24-point letters span well over twenty pixels; one letter drawn twice
            // spans about half that.
            int span = last - first;
            bool ok = span > 20;

            Diagnosis = ok
                ? $"text draws correctly (two letters span {span}px)"
                : $"glyphs do not advance (two letters span only {span}px)";

            return ok;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Diagnosis = $"the probe failed: {ex.Message}";

            return false;
        }
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
