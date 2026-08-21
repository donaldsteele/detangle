using Avalonia.Svg.Skia;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Gives the diagram renderer a font lookup that always answers.
/// <para>
/// Svg.Skia ships two typeface providers and asks each in turn. Both resolve the family and
/// then check the face they got back corresponds to the name asked for; a face that fails
/// that check is discarded and the provider returns nothing. On a platform with one font
/// the check can never pass — WebAssembly resolves every family to the single embedded
/// face, whose name is not the requested one and which is also the platform default — so
/// both providers return null for every family a diagram names.
/// </para>
/// <para>
/// A null from the providers is not itself what breaks the drawing. What breaks it is
/// downstream, in Svg.Skia's per-character font fallback: when no font claims a character
/// it clears the running font's typeface, a font with no typeface reports no metrics, and
/// the span advances measured against it all come back zero. The caller positions each span
/// by accumulating those advances, so an entire label is painted at one x. The full
/// derivation, a reproduction and a patch are in tools/repro/.
/// </para>
/// <para>
/// Answering before the built-ins keeps the renderer out of that path, because the family
/// resolves and the per-character fallback is never asked. It is a workaround for a defect
/// in a dependency, and it is a cheap and total one: <see cref="SvgTextSelfTest"/> measures
/// four of sixteen probe documents drawing correctly without it and sixteen of sixteen with
/// it, on the same platform, with the same face.
/// </para>
/// </summary>
public static class DiagramTypefaces
{
    /// <summary>
    /// Puts the permissive lookup in front of Svg.Skia's own for every diagram the reader
    /// draws.
    /// <para>
    /// The Avalonia SVG control renders through one shared model and copies its settings
    /// into each document it loads, so installing there covers every diagram without
    /// threading configuration through the render path. Idempotent, and cheap enough to
    /// call before each load rather than relying on a startup hook a head might forget.
    /// </para>
    /// </summary>
    public static void InstallShared() => Install(SvgSource.s_skiaModel.Settings);

    /// <summary>
    /// Puts the permissive lookup in front of Svg.Skia's own, on one renderer.
    /// </summary>
    /// <param name="settings">The settings of the renderer about to read a document.</param>
    public static void Install(SKSvgSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.TypefaceProviders ??= [];

        if (settings.TypefaceProviders.Any(provider => provider is PermissiveTypefaceProvider))
        {
            return;
        }

        // Ahead of the built-ins rather than behind them: they answer null rather than
        // declining, so a provider placed after them is never reached.
        settings.TypefaceProviders.Insert(0, new PermissiveTypefaceProvider());
    }

    /// <summary>
    /// Resolves a family list to the nearest face the platform has, and never to nothing.
    /// <para>
    /// The names are tried in the order the document wrote them, which is what a font
    /// stack means. What it does not do is second-guess the answer: if the platform says
    /// this is the closest it has to "Inter", that is the face, whatever it is called.
    /// Refusing it does not produce a better font — it produces no font, and a label drawn
    /// with no font is the defect this exists to avoid.
    /// </para>
    /// </summary>
    private sealed class PermissiveTypefaceProvider : ITypefaceProvider
    {
        public SKTypeface? FromFamilyName(
            string fontFamily,
            SKFontStyleWeight fontWeight,
            SKFontStyleWidth fontWidth,
            SKFontStyleSlant fontStyle)
        {
            foreach (string name in Families(fontFamily))
            {
                SKTypeface? typeface = SKTypeface.FromFamilyName(name, fontWeight, fontWidth, fontStyle);

                if (typeface is not null)
                {
                    return typeface;
                }
            }

            // Not null. A caller that gets null here draws the smudge.
            return SKTypeface.FromFamilyName(null, fontWeight, fontWidth, fontStyle) ?? SKTypeface.Default;
        }

        private static IEnumerable<string> Families(string fontFamily)
        {
            if (string.IsNullOrWhiteSpace(fontFamily))
            {
                yield break;
            }

            foreach (string name in fontFamily.Split(','))
            {
                string trimmed = name.Trim().Trim('\'', '"');

                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }
}
