using System.Globalization;
using System.Text.RegularExpressions;
using Detangle.Core.Dbml;
using Mermaider;
using Mermaider.Models;

// Detangle.Rendering has a RenderOptions of its own, and the parent namespace wins in an
// unqualified reference; the alias keeps both readable.
using MermaidOptions = Mermaider.Models.RenderOptions;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// The default diagram backend: Mermaider, in process, no runtime dependencies
/// (plan.md section 4.1).
/// <para>
/// DBML takes the same path. It is parsed by Detangle's own parser, emitted as Mermaid
/// <c>erDiagram</c> source, and handed to the same renderer — one rendering path, and the
/// lossy part of the translation stays in the emitter where it is visible and tested.
/// </para>
/// <para>
/// Nothing here throws for bad diagram source. A malformed fence returns a result whose
/// SVG is empty and whose diagnostics say why, and the control factory draws that as an
/// error card with the offending source beside it.
/// </para>
/// </summary>
public sealed partial class MermaiderDiagramRenderer : IDiagramRenderer
{
    /// <inheritdoc />
    /// <remarks>Always true: the backend is pure .NET and has nothing to probe for.</remarks>
    public bool IsAvailable => true;

    /// <summary>The name shown in the settings UI.</summary>
    public static string DisplayName => "Mermaider (in-process)";

    /// <inheritdoc />
    public Task<DiagramResult> RenderAsync(
        DiagramKind kind,
        string source,
        DiagramTheme theme,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Render(kind, source, theme));
    }

    /// <summary>Renders synchronously; Mermaider is fast enough that async is a courtesy.</summary>
    public DiagramResult Render(DiagramKind kind, string source, DiagramTheme theme)
    {
        var diagnostics = new List<string>();
        string mermaid = source;

        if (kind == DiagramKind.Dbml)
        {
            DbmlSchema schema = DbmlParser.Parse(source);

            foreach (DbmlDiagnostic diagnostic in schema.Diagnostics)
            {
                diagnostics.Add(diagnostic.ToString());
            }

            if (schema.IsEmpty)
            {
                return new DiagramResult(string.Empty, 0, 0, diagnostics.Count > 0
                    ? diagnostics
                    : ["This DBML block declares no tables."]);
            }

            diagnostics.AddRange(MermaidErdEmitter.UnrepresentedFeatures(schema)
                .Select(feature => $"Not shown in the diagram: {feature}."));

            mermaid = MermaidErdEmitter.Emit(schema);
        }

        try
        {
            string svg = MermaidRenderer.RenderSvg(NormalizeHeader(mermaid), OptionsFor(theme));
            (double width, double height) = MeasureSvg(svg);

            return new DiagramResult(svg, width, height, diagnostics);
        }
        catch (MermaidResourceLimitException ex)
        {
            diagnostics.Add($"This diagram is too large to render: {ex.Message}");
        }
        catch (MermaidParseException ex)
        {
            // The SVG and resource-limit exceptions derive from this one, so every way
            // Mermaider can refuse a diagram becomes an inline card rather than a crash.
            diagnostics.Add(ex.Message);
        }

        return new DiagramResult(string.Empty, 0, 0, diagnostics);
    }

    /// <summary>
    /// Strips a trailing semicolon from a diagram's header line.
    /// <para>
    /// "graph TD;" is everywhere in the wild — mermaid.js accepts it and the Mermaid docs
    /// use it — but Mermaider rejects the header outright and the whole diagram fails.
    /// Normalizing one character here is the difference between a rendered flowchart and
    /// an error card on a page whose source is perfectly valid Mermaid.
    /// </para>
    /// </summary>
    internal static string NormalizeHeader(string source)
    {
        int start = 0;

        while (start < source.Length && char.IsWhiteSpace(source[start]))
        {
            start++;
        }

        int end = source.IndexOf('\n', start);
        string header = (end < 0 ? source[start..] : source[start..end]).TrimEnd();

        if (!header.EndsWith(';'))
        {
            return source;
        }

        return string.Concat(source.AsSpan(0, start), header.AsSpan(0, header.Length - 1),
            end < 0 ? string.Empty : source[end..]);
    }

    /// <summary>
    /// Maps Detangle's palette onto Mermaider's. Themed rendering is why the cache key
    /// includes the theme: the same source produces a different SVG in dark mode.
    /// </summary>
    private static MermaidOptions OptionsFor(DiagramTheme theme) => theme == DiagramTheme.Dark
        ? new MermaidOptions
        {
            Bg = "#16181b",
            Fg = "#e6e6e3",
            Line = "#6b7681",
            Accent = "#6cb6ff",
            Muted = "#9aa4b0",
            Surface = "#1e2125",
            Border = "#31363c",
            Transparent = true,
        }
        : new MermaidOptions
        {
            Bg = "#fdfdfc",
            Fg = "#1a1d21",
            Line = "#7d868f",
            Accent = "#1f6feb",
            Muted = "#5c6570",
            Surface = "#f3f3f1",
            Border = "#dcdcd8",
            Transparent = true,
        };

    /// <summary>
    /// Reads the intrinsic size out of the SVG root, preferring the viewBox because a
    /// width given in "100%" or in millimetres tells the layout nothing useful. Public
    /// because the cache reads it back for SVG it did not render itself.
    /// </summary>
    public static (double Width, double Height) MeasureSvg(string svg)
    {
        Match viewBox = ViewBoxPattern().Match(svg);

        if (viewBox.Success)
        {
            string[] parts = viewBox.Groups["box"].Value
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double boxWidth)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double boxHeight))
            {
                return (boxWidth, boxHeight);
            }
        }

        return (ReadLength(svg, "width"), ReadLength(svg, "height"));
    }

    private static double ReadLength(string svg, string attribute)
    {
        Match match = Regex.Match(
            svg,
            $"<svg[^>]*\\s{attribute}=\"(?<value>[0-9.]+)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        return match.Success
            && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0;
    }

    [GeneratedRegex(@"<svg[^>]*\sviewBox=""(?<box>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBoxPattern();
}
