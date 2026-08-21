namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Asks the configuration the reader ships, once, whether it can draw SVG text correctly.
/// <para>
/// This began as a permanent verdict about WebAssembly and is now a safety net, because
/// the defect it was written for has a fix. Naming a font family used to collapse every
/// glyph of a label onto one point there; <see cref="DiagramTypefaces"/> repairs it, and
/// this probe measures with that repair installed, so on the platform it was written for
/// it now reads healthy. What it guards is the case where some future platform breaks the
/// same way and the fix does not take: <see cref="SvgStyleFlattener.RemoveFontFamilies"/>
/// still draws readable labels there, at the cost of the typeface.
/// </para>
/// <para>
/// The probe renders two letters styled through a style block — the delivery Mermaider
/// emits — and checks the second landed to the right of the first. Every cheaper question
/// answered wrongly: WebAssembly reported a font family, resolved "sans-serif" to a real
/// face with 897 glyphs, and measured a nine-letter word at 72 pixels while drawing all
/// nine letters on top of each other. Where the ink fell is the only question that
/// survived. <see cref="SvgTextSelfTest"/> is the same measurement across every delivery,
/// size and weight, and <see cref="SvgTextLayerProbe"/> is what located the cause.
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

            // Measured with the font lookup the reader actually draws diagrams through.
            // Asking about the bare platform would answer a question nothing depends on:
            // what matters is whether the configuration that ships can draw a label.
            SvgInkSpan.Reading reading = SvgInkSpan.Measure(Probe, Width, Height, DiagramTypefaces.Install);

            if (!reading.Parsed)
            {
                Diagnosis = "the probe did not parse";

                return false;
            }

            if (reading.First < 0)
            {
                Diagnosis = "no text was drawn at all";

                return false;
            }

            // Two 24-point letters span well over twenty pixels; one letter drawn twice
            // spans about half that.
            int span = reading.Span;
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
}
