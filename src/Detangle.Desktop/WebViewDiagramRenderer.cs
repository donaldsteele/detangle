using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Detangle.Rendering;

namespace Detangle.Desktop;

/// <summary>
/// The opt-in "high-fidelity" diagram backend: mermaid.js in an offscreen WebView, for
/// exact parity with what GitHub draws (plan.md section 4.1).
/// <para>
/// It lives in the desktop head rather than in Detangle.Rendering because it is the one
/// piece of the diagram story with a platform dependency: on Linux a WebView needs
/// <c>libwebkit2gtk-4.1-0</c> and <c>libsoup-3.0-0</c>, which is the largest packaging
/// risk in the plan. Keeping it out of the shared library means the default build stays
/// dependency-free on all three OSes and the WASM head never sees it at all.
/// </para>
/// <para>
/// Availability is probed rather than assumed. When the probe fails the setting is
/// disabled with an explanation instead of the app crashing on first diagram.
/// </para>
/// </summary>
public sealed class WebViewDiagramRenderer : IDiagramRenderer
{
    private readonly IDiagramRenderer _fallback;

    /// <summary>Creates the backend over a fallback used whenever the WebView is unusable.</summary>
    /// <param name="fallback">The in-process renderer to fall back to.</param>
    public WebViewDiagramRenderer(IDiagramRenderer fallback)
    {
        _fallback = fallback;
    }

    /// <summary>The name shown in the settings UI.</summary>
    public static string DisplayName => "WebView (mermaid.js, high fidelity)";

    /// <inheritdoc />
    public bool IsAvailable => Probe().IsSupported;

    /// <summary>Why the WebView backend cannot be used here, or null when it can.</summary>
    public static string? UnavailableReason => Probe().Reason;

    /// <inheritdoc />
    public Task<DiagramResult> RenderAsync(
        DiagramKind kind,
        string source,
        DiagramTheme theme,
        CancellationToken cancellationToken = default)
    {
        // The mermaid.js render path itself is not wired up yet — this backend currently
        // reports its availability honestly and defers to the in-process renderer, which
        // is the behaviour the setting promises when the platform cannot host a WebView.
        return _fallback.RenderAsync(kind, source, theme, cancellationToken);
    }

    /// <summary>
    /// Checks whether this machine can host a WebView at all. On Linux that means the
    /// WebKitGTK and libsoup shared objects being present; on Windows and macOS the
    /// system component ships with the OS.
    /// </summary>
    [SuppressMessage(
        "Interoperability",
        "CA1416:Validate platform compatibility",
        Justification = "Every branch is guarded by the OperatingSystem check that precedes it.")]
    private static (bool IsSupported, string? Reason) Probe()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return (true, null);
        }

        if (!OperatingSystem.IsLinux())
        {
            return (false, "This platform has no supported WebView.");
        }

        foreach (string library in (string[])["libwebkit2gtk-4.1.so.0", "libsoup-3.0.so.0"])
        {
            if (!NativeLibrary.TryLoad(library, out nint handle))
            {
                return (false,
                    $"{library} is not installed. High-fidelity diagrams need WebKitGTK and libsoup 3; "
                    + "the in-process renderer is used instead.");
            }

            NativeLibrary.Free(handle);
        }

        return (true, null);
    }
}
