using System.Globalization;
using System.Text;
using Svg.Skia;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// The full variant matrix behind <see cref="SvgTextCapability"/>'s one-bit answer.
/// <para>
/// The capability probe asks a single question — can this platform draw text whose family
/// arrived through CSS — because that is all the renderer needs to decide. Confirming the
/// defect, or confirming a fix, needs more: which deliveries break, at which sizes, at
/// which weights. That is what this produces, as a table any console can print.
/// </para>
/// <para>
/// Every row is measured through <see cref="SvgInkSpan"/>, the same rasteriser the gate
/// uses, so the matrix and the gate can never disagree.
/// </para>
/// </summary>
public static class SvgTextSelfTest
{
    private const int Width = 320;
    private const int Height = 48;
    private const int Baseline = 36;

    /// <summary>How many letters the wide sample repeats.</summary>
    private const int Repeats = 4;

    /// <summary>
    /// A ratio at or above this means the glyphs advanced. A correct renderer scores about
    /// <see cref="Repeats"/>; one that stacks every glyph at one position scores about 1.
    /// Halfway between the two is a wide gap to sit in.
    /// </summary>
    private const double AdvancingRatio = 2.0;

    /// <summary>One cell of the matrix, and what the raster showed.</summary>
    /// <param name="Delivery">How the font family reached the text, or "none".</param>
    /// <param name="FontSize">The font-size the sample asked for.</param>
    /// <param name="Weight">The font-weight the sample asked for.</param>
    /// <param name="SingleSpan">Ink span of one letter, in pixels, or -1 for no ink.</param>
    /// <param name="RepeatedSpan">Ink span of the same letter repeated, or -1.</param>
    /// <param name="Verdict">What those two spans mean.</param>
    public readonly record struct Sample(
        string Delivery,
        int FontSize,
        string Weight,
        int SingleSpan,
        int RepeatedSpan,
        string Verdict)
    {
        /// <summary>
        /// Repeated span over single span: how many letter-widths the word occupies.
        /// <para>
        /// This is the measure that makes the matrix comparable across sizes and weights.
        /// An absolute pixel threshold cannot be: two letters at 12 points span less than
        /// one letter at 24, so any fixed number is either blind at the small size or a
        /// false alarm at the large one. Dividing by a single letter measured in the very
        /// same style calibrates the test against itself.
        /// </para>
        /// </summary>
        public double Ratio => SingleSpan > 0 ? (double)RepeatedSpan / SingleSpan : 0;

        /// <summary>True when the letters advanced instead of stacking.</summary>
        public bool Advancing => Ratio >= AdvancingRatio;
    }

    /// <summary>Runs every cell of the matrix and reports what each one drew.</summary>
    public static IReadOnlyList<Sample> Run() => Run(settings: null);

    /// <summary>
    /// Runs every cell with <paramref name="settings"/> applied to the renderer first, so
    /// the same sixteen documents can be measured with a repair in place and without one.
    /// </summary>
    public static IReadOnlyList<Sample> Run(Action<SKSvgSettings>? settings)
    {
        var samples = new List<Sample>();

        // Four ways a family can reach a <text>, so a row that collapses names the
        // delivery that did it rather than leaving "font-family" as the whole answer.
        foreach (string delivery in new[] { "none", "attribute", "style-attr", "css" })
        {
            foreach (int size in new[] { 12, 24 })
            {
                foreach (string weight in new[] { "400", "700" })
                {
                    samples.Add(Measure(delivery, size, weight, settings));
                }
            }
        }

        return samples;
    }

    /// <summary>
    /// The matrix rendered as a fixed-width table, with the gate's own verdict under it.
    /// <para>
    /// Written for a browser console, which is the only output device the WebAssembly
    /// build has.
    /// </para>
    /// </summary>
    public static string Table() => Table(settings: null);

    /// <summary>The matrix measured with <paramref name="settings"/> applied.</summary>
    public static string Table(Action<SKSvgSettings>? settings)
    {
        IReadOnlyList<Sample> samples = Run(settings);
        var text = new StringBuilder();

        text.AppendLine("detangle svg text self-test");
        text.AppendLine(
            "  each row draws \"M\" and \"MMMM\" in one style and measures how far the ink reaches.");
        text.AppendLine(
            "  ratio is MMMM's span over M's span: about 4 when glyphs advance, about 1 when they stack.");
        text.AppendLine();
        text.AppendLine("  family via   size  weight   M span   MMMM span   ratio   verdict");
        text.AppendLine("  ----------   ----  ------   ------   ---------   -----   -------");

        foreach (Sample sample in samples)
        {
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {sample.Delivery,-10}   {sample.FontSize,4}  {sample.Weight,6}   "
                + $"{sample.SingleSpan,6}   {sample.RepeatedSpan,9}   {sample.Ratio,5:0.00}   {sample.Verdict}"));
        }

        int collapsed = samples.Count(sample => !sample.Advancing);

        text.AppendLine();
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {samples.Count - collapsed}/{samples.Count} variants draw advancing glyphs."));
        text.AppendLine(
            $"  gate: SvgTextCapability.CanDrawText={SvgTextCapability.CanDrawText} - {SvgTextCapability.Diagnosis}");
        text.AppendLine(
            "  the gate strips font-family from diagram SVG when it reads false; the 'none' rows are what it leaves behind.");

        return text.ToString();
    }

    private static Sample Measure(string delivery, int size, string weight, Action<SKSvgSettings>? settings)
    {
        SvgInkSpan.Reading single = SvgInkSpan.Measure(Document(delivery, size, weight, 1), Width, Height, settings);
        SvgInkSpan.Reading repeated = SvgInkSpan.Measure(
            Document(delivery, size, weight, Repeats), Width, Height, settings);

        string verdict = !single.Parsed || !repeated.Parsed
            ? "did not parse"
            : single.First < 0
                ? "no ink at all"
                : (double)repeated.Span / single.Span >= AdvancingRatio
                    ? "advancing"
                    : "glyphs stacked";

        return new Sample(delivery, size, weight, single.Span, repeated.Span, verdict);
    }

    /// <summary>
    /// Builds one probe document. Everything but the delivery of the family is held
    /// constant, so a row that differs from its neighbour differs for one reason.
    /// </summary>
    private static string Document(string delivery, int size, string weight, int letters)
    {
        string style = delivery == "css" ? "<style>text { font-family: sans-serif; }</style>" : string.Empty;

        string attribute = delivery switch
        {
            "attribute" => " font-family=\"sans-serif\"",
            "style-attr" => " style=\"font-family: sans-serif\"",
            _ => string.Empty,
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\">{style}"
            + $"<text x=\"2\" y=\"{Baseline}\" font-size=\"{size}\" font-weight=\"{weight}\"{attribute} "
            + $"fill=\"#ffffff\">{new string('M', letters)}</text></svg>");
    }
}
