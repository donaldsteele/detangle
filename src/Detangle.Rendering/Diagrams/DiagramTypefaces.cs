using Avalonia.Svg.Skia;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace Detangle.Rendering.Diagrams;

/// <summary>
/// Gives the diagram renderer a font lookup that always answers.
/// <para>
/// Svg.Skia ships two typeface providers and asks each in turn. Both do the same thing:
/// resolve the family, then check the face they got back actually corresponds to the name
/// that was asked for — its family name matches, or it is the platform default answering
/// to its own name, or a generic name resolved to something other than the default. A face
/// that passes none of those is discarded and the provider returns nothing.
/// </para>
/// <para>
/// On a platform with one font, that test can never pass. WebAssembly resolves every
/// family — "sans-serif", "Inter", anything — to the single embedded face, Noto Mono. Its
/// name is not the name that was asked for, and it *is* the platform default, which is
/// what disqualifies it under the generic-name rule as well. So both providers return
/// null for every family a diagram names, and the drawing that follows a null typeface is
/// the one that paints every glyph of a label at the same point.
/// </para>
/// <para>
/// The remedy is to answer before they do, with the best match the platform can offer and
/// no opinion about what it is called. <see cref="SvgTextLayerProbe"/> measures it: the
/// same document that spans 12 pixels through the default lookup spans 55 through this
/// one, on the same platform, with the same face.
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
