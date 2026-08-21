using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace SvgTextWasmRepro;

/// <summary>
/// Reproduces, under browser-wasm, a diagram label collapsing into a single mark.
/// <para>
/// Everything here is measured rather than described: each case renders "M" and "MMMM" and
/// reports the ratio of the horizontal extent of the ink. Glyphs that advance score about
/// four; glyphs painted on top of each other score about one. That ratio is used rather
/// than a pixel count because a label normally sits on an opaque background, so counting
/// coloured pixels cannot tell a drawn word from an undrawn one — and it is used rather
/// than an absolute width because two letters at 12pt span less than one at 24pt.
/// </para>
/// </summary>
internal static class Program
{
    private const int Width = 320;
    private const int Height = 48;
    private const int Size = 24;
    private const int Baseline = 36;

    private static void Main()
    {
        Console.WriteLine($"SkiaSharp default typeface: {Describe(SKTypeface.Default)}");
        Console.WriteLine($"resolved for \"sans-serif\": {Describe(SKTypeface.FromFamilyName("sans-serif"))}");
        Console.WriteLine();

        Console.WriteLine("what Svg.Skia's built-in providers answer");

        foreach (string family in new[] { "sans-serif", "monospace", "Inter" })
        {
            Console.WriteLine($"  \"{family}\"");
            Console.WriteLine($"    FontManagerTypefaceProvider  {Answer(new FontManagerTypefaceProvider(), family)}");
            Console.WriteLine($"    DefaultTypefaceProvider      {Answer(new DefaultTypefaceProvider(), family)}");
        }

        Console.WriteLine();
        Console.WriteLine("case                                 M    MMMM   ratio   verdict");

        Report("no font-family named", Document(family: null), settings: null);
        Report("font-family=\"sans-serif\"", Document("sans-serif"), settings: null);

        // A family named outright, and a stack ending in a generic. This is what a diagram
        // generator actually emits — Mermaid asks for Inter — so a fix that only rescues
        // the bare generic would leave real documents broken.
        Report("font-family=\"Inter\"", Document("Inter"), settings: null);
        Report("font-family=\"Inter, sans-serif\"", Document("Inter, sans-serif"), settings: null);

        Report("font-family=\"sans-serif\" + provider", Document("sans-serif"), Install);
        Report("font-family=\"Inter\" + provider", Document("Inter"), Install);

        // Drawn straight onto a canvas with the same face the SVG cases ask for, so the
        // font and SkiaSharp itself are ruled in or out without Svg.Skia in the way.
        using SKTypeface? face = SKTypeface.FromFamilyName("sans-serif");

        if (face is not null)
        {
            (int single, int repeated) = (DrawDirect(face, 1), DrawDirect(face, 4));

            Print("SKCanvas.DrawText, no SVG", single, repeated);
        }

        Console.WriteLine();
        Console.WriteLine("repro complete");
    }

    /// <summary>
    /// A typeface provider with no opinion about what the face it found is called. This is
    /// the whole of the workaround, and the shape of the suggested fix.
    /// </summary>
    private sealed class PermissiveTypefaceProvider : ITypefaceProvider
    {
        public SKTypeface? FromFamilyName(
            string fontFamily,
            SKFontStyleWeight fontWeight,
            SKFontStyleWidth fontWidth,
            SKFontStyleSlant fontStyle)
        {
            foreach (string name in (fontFamily ?? string.Empty).Split(','))
            {
                string trimmed = name.Trim().Trim('\'', '"');

                if (trimmed.Length > 0
                    && SKTypeface.FromFamilyName(trimmed, fontWeight, fontWidth, fontStyle) is { } typeface)
                {
                    return typeface;
                }
            }

            return SKTypeface.FromFamilyName(null, fontWeight, fontWidth, fontStyle) ?? SKTypeface.Default;
        }
    }

    private static void Install(SKSvgSettings settings)
    {
        settings.TypefaceProviders ??= [];
        settings.TypefaceProviders.Insert(0, new PermissiveTypefaceProvider());
    }

    private static void Report(string label, Func<int, string> document, Action<SKSvgSettings>? settings)
    {
        int single = MeasureSvg(document(1), settings);
        int repeated = MeasureSvg(document(4), settings);

        Print(label, single, repeated);
    }

    private static void Print(string label, int single, int repeated)
    {
        double ratio = single > 0 ? (double)repeated / single : 0;
        string verdict = single <= 0 ? "no ink at all"
            : ratio >= 2.0 ? "advancing"
            : "GLYPHS STACKED";

        Console.WriteLine($"{label,-36} {single,3}  {repeated,6}   {ratio,5:0.00}   {verdict}");
    }

    private static Func<int, string> Document(string? family) => letters =>
    {
        string attribute = family is null ? string.Empty : $" font-family=\"{family}\"";

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\">"
            + $"<text x=\"2\" y=\"{Baseline}\" font-size=\"{Size}\"{attribute} fill=\"#ffffff\">"
            + new string('M', letters)
            + "</text></svg>";
    };

    private static int MeasureSvg(string svg, Action<SKSvgSettings>? settings)
    {
        using var picture = new SKSvg();

        settings?.Invoke(picture.Settings);

        if (picture.FromSvg(svg) is null || picture.Picture is null)
        {
            return -1;
        }

        return Measure(canvas => canvas.DrawPicture(picture.Picture));
    }

    private static int DrawDirect(SKTypeface typeface, int letters) =>
        Measure(canvas =>
        {
            using var font = new SKFont(typeface, Size);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };

            canvas.DrawText(new string('M', letters), 2, Baseline, SKTextAlign.Left, font, paint);
        });

    /// <summary>Draws in white on black and returns the width of the inked columns.</summary>
    private static int Measure(Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(Width, Height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            draw(canvas);
        }

        int first = -1;
        int last = -1;

        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).Red > 100)
                {
                    first = first < 0 ? x : first;
                    last = x;

                    break;
                }
            }
        }

        return first < 0 ? -1 : last - first;
    }

    private static string Answer(ITypefaceProvider provider, string family)
    {
        SKTypeface? typeface = provider.FromFamilyName(
            family, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        return typeface is null ? "null  <- declines to answer" : $"\"{typeface.FamilyName}\"";
    }

    private static string Describe(SKTypeface? typeface)
    {
        if (typeface is null)
        {
            return "not resolved";
        }

        using SKStreamAsset? stream = typeface.OpenStream(out int index);

        return $"\"{typeface.FamilyName}\", {typeface.GlyphCount} glyphs, "
            + (stream is null ? "no stream" : $"{stream.Length}-byte stream, ttc index {index}");
    }
}
