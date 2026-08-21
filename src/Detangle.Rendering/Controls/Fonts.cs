using Avalonia.Media;

namespace Detangle.Rendering.Controls;

/// <summary>
/// The faces the reader draws with, addressed by resource URI.
/// <para>
/// Compiled into the assembly rather than looked up by name, for two reasons. The
/// WebAssembly build has no system fonts to search at all, so a family name resolves to
/// nothing and falls through to whatever the runtime embeds. And the faces that do ship
/// with desktop systems are missing the mathematics: Segoe UI, Arial and Cascadia all
/// lack the transpose sign, the tensor product, angle brackets and half the set notation,
/// and draw an empty box where the symbol should be.
/// </para>
/// <para>
/// Licences are recorded in THIRD-PARTY-NOTICES.md at the repository root.
/// </para>
/// </summary>
public static class Fonts
{
    private const string Assets = "avares://Detangle.Rendering/Assets/Fonts";

    /// <summary>Body prose. Inter, from the Avalonia font package the app already carries.</summary>
    public static FontFamily Body { get; } = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    /// <summary>
    /// Code, paths and every other identifier. DejaVu Sans Mono: unremarkable to look at,
    /// which is what a face carrying file paths should be, and it covers the symbols.
    /// </summary>
    public static FontFamily Mono { get; } = new($"{Assets}#DejaVu Sans Mono");

    /// <summary>
    /// Mathematics. DejaVu Math TeX Gyre covers the notation completely — the reason the
    /// transpose sign can be the real character rather than a stand-in letter.
    /// </summary>
    public static FontFamily Math { get; } = new($"{Assets}#DejaVu Math TeX Gyre");
}
