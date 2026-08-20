namespace Detangle.Rendering;

/// <summary>The diagram languages Detangle renders.</summary>
public enum DiagramKind
{
    Mermaid,
    Dbml,
}

/// <summary>Which palette a diagram should be rendered against.</summary>
public enum DiagramTheme
{
    Light,
    Dark,
}

/// <summary>
/// The result of a diagram render. Always SVG: rendering once to SVG and caching
/// is what keeps scrolling native and makes the backends interchangeable.
/// </summary>
/// <param name="Svg">The rendered SVG document.</param>
/// <param name="Width">Intrinsic width in device-independent pixels.</param>
/// <param name="Height">Intrinsic height in device-independent pixels.</param>
/// <param name="Diagnostics">Parse or layout warnings; empty on a clean render.</param>
public sealed record DiagramResult(
    string Svg,
    double Width,
    double Height,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Source string in, SVG string out. Implemented three times (plan.md section 4.1):
/// Mermaider in-process is the default, an offscreen WebView is the opt-in
/// high-fidelity mode, and browser JS interop backs the WASM demo.
/// </summary>
public interface IDiagramRenderer
{
    /// <summary>True when this backend can run in the current environment.</summary>
    bool IsAvailable { get; }

    /// <summary>Renders diagram source to SVG.</summary>
    Task<DiagramResult> RenderAsync(
        DiagramKind kind,
        string source,
        DiagramTheme theme,
        CancellationToken cancellationToken = default);
}
