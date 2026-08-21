using System.Globalization;
using System.Text;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia.TypefaceProviders;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Finds which layer stacks the glyphs, and settles whether naming a real face fixes it.
/// <para>
/// <see cref="SvgTextSelfTest"/> establishes what breaks: every delivery of a font family
/// collapses, and only text with no family named survives. It cannot say where, because
/// every one of its rows goes through the whole stack — Svg.Skia parses, Svg.Model builds
/// a scene, SkiaSharp draws, and HarfBuzz shapes. This probe takes that stack apart.
/// </para>
/// <para>
/// Two questions, and the answers are mutually exclusive enough to decide between the two
/// standing theories.
/// </para>
/// <list type="number">
/// <item>
/// Does SkiaSharp advance glyphs when it is handed a real typeface directly, with no SVG
/// anywhere? Reading Svg.Skia's source says the collapse is its HarfBuzz branch, which is
/// entered only when the typeface has an openable stream — a plain
/// <c>SKCanvas.DrawText</c> never goes near it. If direct drawing advances and SVG does
/// not, the defect is above SkiaSharp.
/// </item>
/// <item>
/// Does registering a bundled font as a typeface provider rescue the named rows? This is
/// the one repair that would keep the author's typeface instead of discarding it, so it is
/// worth more than the workaround if it works. Reading the source predicts it makes things
/// worse rather than better: a bundled TTF is exactly the openable stream that routes the
/// run into the shaper. That prediction is cheap to test and expensive to assume.
/// </item>
/// </list>
/// </summary>
public static class SvgTextLayerProbe
{
    private const int Width = 320;
    private const int Height = 48;
    private const int Baseline = 36;
    private const int Size = 24;

    /// <summary>How many letters the wide sample repeats.</summary>
    private const int Repeats = 4;

    /// <summary>Matches <see cref="SvgTextSelfTest"/>, so both tables read the same way.</summary>
    private const double AdvancingRatio = 2.0;

    private const string Mono = "avares://Detangle.Rendering/Assets/Fonts/DejaVuSansMono.ttf";

    /// <summary>What one probe drew, whatever drew it.</summary>
    /// <param name="Layer">Which stack the sample went through.</param>
    /// <param name="Face">Which typeface it asked for.</param>
    /// <param name="SingleSpan">Ink span of one letter, in pixels, or -1 for no ink.</param>
    /// <param name="RepeatedSpan">Ink span of the same letter repeated, or -1.</param>
    public readonly record struct Sample(string Layer, string Face, int SingleSpan, int RepeatedSpan)
    {
        /// <summary>Repeated span over single span: how many letter-widths the word occupies.</summary>
        public double Ratio => SingleSpan > 0 ? (double)RepeatedSpan / SingleSpan : 0;

        /// <summary>What those two spans mean.</summary>
        public string Verdict => SingleSpan < 0
            ? "no ink at all"
            : Ratio >= AdvancingRatio ? "advancing" : "glyphs stacked";
    }

    /// <summary>
    /// The probe rendered as a table, with the typeface facts it rests on printed above it.
    /// Written for a browser console, which is the only output device that matters here.
    /// </summary>
    public static string Table()
    {
        var text = new StringBuilder();

        text.AppendLine("detangle svg text layer probe");
        text.AppendLine("  which layer stacks the glyphs, and whether naming a real face repairs it.");
        text.AppendLine();

        text.AppendLine("  typefaces this platform offers");
        text.AppendLine("  ------------------------------");

        SKTypeface? fallback = SKTypeface.Default;
        SKTypeface? matched = Match("sans-serif");
        (SKTypeface? bundled, string? failure) = Bundled();

        text.AppendLine("  default      " + Describe(fallback));
        text.AppendLine("  sans-serif   " + Describe(matched));
        text.AppendLine("  bundled ttf  " + (failure ?? Describe(bundled)));
        text.AppendLine();

        // The stream is the whole question. Svg.Skia shapes with HarfBuzz whenever it can
        // open one, and falls back to Skia's own text drawing when it cannot, so which of
        // these faces has one predicts which rows below will collapse.
        text.AppendLine("  a face with an openable stream is the one Svg.Skia shapes with HarfBuzz;");
        text.AppendLine("  a face without one falls through to SkiaSharp's own text drawing.");
        text.AppendLine();

        // Asking the built-in providers directly, rather than inferring what they did from
        // what got drawn. They are public types, so this is the actual answer the renderer
        // receives for the family a diagram names.
        text.AppendLine("  what Svg.Skia's own providers answer");
        text.AppendLine("  -----------------------------------");

        foreach (string family in new[] { "sans-serif", "Inter", "monospace" })
        {
            text.AppendLine($"  \"{family}\"");
            text.AppendLine("    FontManagerTypefaceProvider  " + Answer(new FontManagerTypefaceProvider(), family));
            text.AppendLine("    DefaultTypefaceProvider      " + Answer(new DefaultTypefaceProvider(), family));
        }

        text.AppendLine();

        text.AppendLine("  layer          face          M span   MMMM span   ratio   verdict");
        text.AppendLine("  -----          ----          ------   ---------   -----   -------");

        foreach (Sample sample in Run(fallback, matched, bundled))
        {
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {sample.Layer,-13}  {sample.Face,-12}  {sample.SingleSpan,6}   "
                + $"{sample.RepeatedSpan,9}   {sample.Ratio,5:0.00}   {sample.Verdict}"));
        }

        bundled?.Dispose();
        matched?.Dispose();

        return text.ToString();
    }

    private static List<Sample> Run(SKTypeface? fallback, SKTypeface? matched, SKTypeface? bundled)
    {
        var samples = new List<Sample>();

        // The provider row, before anything else has drawn. Its result later in the run is
        // ambiguous on its own: Svg.Skia's built-in providers dispose a typeface they
        // decide not to use, so a provider asked later could be handing over a dead handle
        // and being rescued by the fallback rather than by the face. Asking first removes
        // that reading — nothing has looked a family up yet, so nothing has been disposed.
        SKTypeface? first = Match("sans-serif");

        if (first is not null)
        {
            AddSvg(
                samples,
                "provider-1st",
                "resolved",
                Document("sans-serif", 1),
                Document("sans-serif", Repeats),
                Provider(first));
        }

        // Straight onto a canvas: no SVG parser, no scene graph, no shaper. This is
        // SkiaSharp answering for itself.
        AddDirect(samples, "skia-direct", "default", fallback);
        AddDirect(samples, "skia-direct", "sans-serif", matched);
        AddDirect(samples, "skia-direct", "bundled", bundled);

        // The same two documents the matrix uses, for a like-for-like comparison in the
        // same table rather than across two of them.
        AddSvg(samples, "svg-default", "none", Document(family: null, letters: 1), Document(family: null, letters: Repeats), settings: null);
        AddSvg(samples, "svg-default", "sans-serif", Document("sans-serif", 1), Document("sans-serif", Repeats), settings: null);

        // And the repair under test: the bundled face registered under the name the
        // document asks for, so the lookup succeeds with a face this application chose
        // rather than whatever the platform had lying around.
        // Falling back to the resolved face is not a compromise here. The question the row
        // asks is whether a lookup that succeeds with a real, stream-backed face draws
        // differently from one that goes through the platform's own path — and any real
        // face answers it. The bundled one is preferred only because it is the face the
        // application would want to keep.
        SKTypeface? registered = bundled ?? matched;

        if (registered is not null)
        {
            AddSvg(
                samples,
                "svg-provider",
                bundled is null ? "resolved" : "bundled",
                Document("sans-serif", 1),
                Document("sans-serif", Repeats),
                Provider(registered));
        }

        // The two rows that decide it. Everything above draws in the order that makes each
        // layer look its best: the direct draws happen before any SVG has been parsed, and
        // the matrix in SvgTextSelfTest runs its family-free rows first. If the defect is
        // that a family lookup disposes the shared typeface, then repeating those two
        // known-good drawings AFTER a lookup has happened must turn them bad — the same
        // code, the same face, the same document, differing only in what ran before them.
        AddDirect(samples, "direct-after", "default", SKTypeface.Default);

        AddSvg(
            samples,
            "svg-after",
            "none",
            Document(family: null, letters: 1),
            Document(family: null, letters: Repeats),
            settings: null);

        return samples;
    }

    /// <summary>
    /// Puts the bundled face in front of the font lookup, answering to the name the probe
    /// document asks for.
    /// </summary>
    private static Action<Svg.Skia.SKSvgSettings> Provider(SKTypeface bundled) => settings =>
    {
        var provider = new BundledTypefaceProvider(bundled);

        settings.TypefaceProviders?.Insert(0, provider);
    };

    private static void AddDirect(List<Sample> samples, string layer, string face, SKTypeface? typeface)
    {
        if (typeface is null)
        {
            // A face that could not be opened has nothing to say about advances. The
            // typeface block above already reports why it is missing, so a row here would
            // only look like a failed drawing.
            return;
        }

        SvgInkSpan.Reading single = DrawDirect(typeface, 1);
        SvgInkSpan.Reading repeated = DrawDirect(typeface, Repeats);

        samples.Add(new Sample(layer, face, single.Span, repeated.Span));
    }

    private static SvgInkSpan.Reading DrawDirect(SKTypeface typeface, int letters)
    {
        return SvgInkSpan.Measure(
            canvas =>
            {
                using var font = new SKFont(typeface, Size);
                using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };

                canvas.DrawText(new string('M', letters), 2, Baseline, SKTextAlign.Left, font, paint);
            },
            Width,
            Height);
    }

    private static void AddSvg(
        List<Sample> samples,
        string layer,
        string face,
        string single,
        string repeated,
        Action<Svg.Skia.SKSvgSettings>? settings)
    {
        SvgInkSpan.Reading one = SvgInkSpan.Measure(single, Width, Height, settings);
        SvgInkSpan.Reading many = SvgInkSpan.Measure(repeated, Width, Height, settings);

        samples.Add(new Sample(layer, face, one.Span, many.Span));
    }

    private static string Document(string? family, int letters)
    {
        string attribute = family is null ? string.Empty : $" font-family=\"{family}\"";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\">"
            + $"<text x=\"2\" y=\"{Baseline}\" font-size=\"{Size}\" font-weight=\"400\"{attribute} "
            + $"fill=\"#ffffff\">{new string('M', letters)}</text></svg>");
    }

    /// <summary>What one provider returns for one family, said plainly.</summary>
    private static string Answer(ITypefaceProvider provider, string family)
    {
        try
        {
            SKTypeface? typeface = provider.FromFamilyName(
                family,
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright);

            return typeface is null
                ? "null - declines to answer"
                : $"\"{typeface.FamilyName}\"";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string Describe(SKTypeface? typeface)
    {
        if (typeface is null)
        {
            return "not resolved";
        }

        string stream;

        try
        {
            using SKStreamAsset? asset = typeface.OpenStream(out int index);

            stream = asset is null
                ? "no stream"
                : string.Create(CultureInfo.InvariantCulture, $"stream {asset.Length} bytes, ttc index {index}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            stream = $"stream threw {ex.GetType().Name}";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\"{typeface.FamilyName}\", {typeface.GlyphCount} glyphs, {typeface.UnitsPerEm} upem, {stream}");
    }

    /// <summary>
    /// Resolves a family the way a document asks for one.
    /// <para>
    /// <c>SKFontManager.MatchFamily</c> looks the name up literally and returns nothing for
    /// a generic, which is not the question: what matters is the face the renderer would
    /// actually end up drawing with, and that is what falling back to the default gives.
    /// </para>
    /// </summary>
    private static SKTypeface? Match(string family)
    {
        try
        {
            return SKTypeface.FromFamilyName(family);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the bundled face, or explains why it could not.
    /// <para>
    /// The probe runs before Avalonia has started — it has to, because the browser head
    /// never returns from starting it — so the asset loader may not be able to answer yet.
    /// That is a reason to report the failure rather than swallow it: a missing bundled
    /// face would otherwise look like a bundled face that did not help.
    /// </para>
    /// </summary>
    private static (SKTypeface? Typeface, string? Failure) Bundled()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri(Mono));

            SKTypeface? typeface = SKTypeface.FromStream(stream);

            return (typeface, typeface is null ? "SKTypeface.FromStream returned nothing" : null);
        }
        catch (Exception ex)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Answers to any family name with the one face it was given.
    /// <para>
    /// <see cref="CustomTypefaceProvider"/> would do this, except that it only answers when
    /// the requested weight, width and slant match the face's own exactly — which makes a
    /// negative result ambiguous, because a row could collapse for want of a bold rather
    /// than for the reason under test. This answers unconditionally, so a row that still
    /// stacks stacked with the bundled face in hand.
    /// </para>
    /// </summary>
    private sealed class BundledTypefaceProvider(SKTypeface typeface) : ITypefaceProvider
    {
        public SKTypeface? FromFamilyName(
            string fontFamily,
            SKFontStyleWeight fontWeight,
            SKFontStyleWidth fontWidth,
            SKFontStyleSlant fontStyle) => typeface;
    }
}
