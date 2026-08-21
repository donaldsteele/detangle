using SkiaSharp;

namespace Detangle.Rendering.Export;

/// <summary>Page geometry and type sizes for a PDF export.</summary>
public sealed record PdfOptions
{
    /// <summary>Where the PDF goes.</summary>
    public required string OutputPath { get; init; }

    /// <summary>The document title, written into the PDF metadata and the cover line.</summary>
    public string Title { get; init; } = "Wiki";

    /// <summary>Page width in points. The default is A4.</summary>
    public float PageWidth { get; init; } = 595;

    /// <summary>Page height in points.</summary>
    public float PageHeight { get; init; } = 842;

    /// <summary>Margin on every side, in points.</summary>
    public float Margin { get; init; } = 54;

    /// <summary>Body text size in points.</summary>
    public float FontSize { get; init; } = 10.5f;

    /// <summary>Write a table of contents page when more than one document is exported.</summary>
    public bool IncludeContents { get; init; } = true;

    /// <summary>Content width, derived from the page and its margins.</summary>
    public float ContentWidth => PageWidth - (Margin * 2);
}

/// <summary>
/// The fonts a PDF export draws with, resolved once.
/// <para>
/// Typefaces are looked up by family with a fallback chain rather than assumed: a CI
/// container has almost no fonts installed, and a PDF that silently comes out blank
/// because a family was missing is worse than one that comes out in the wrong face.
/// Skia's default typeface is always available, so the chain ends there.
/// </para>
/// </summary>
internal sealed class PdfTypography : IDisposable
{
    private static readonly string[] BodyFamilies =
        ["Segoe UI", "Helvetica Neue", "Helvetica", "Arial", "DejaVu Sans", "Liberation Sans"];

    private static readonly string[] MonoFamilies =
        ["Cascadia Mono", "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New"];

    private readonly List<SKTypeface> _owned = [];

    public PdfTypography()
    {
        Regular = Resolve(BodyFamilies, SKFontStyle.Normal);
        Bold = Resolve(BodyFamilies, SKFontStyle.Bold);
        Italic = Resolve(BodyFamilies, SKFontStyle.Italic);
        BoldItalic = Resolve(BodyFamilies, SKFontStyle.BoldItalic);
        Mono = Resolve(MonoFamilies, SKFontStyle.Normal);
        MonoBold = Resolve(MonoFamilies, SKFontStyle.Bold);
    }

    public SKTypeface Regular { get; }

    public SKTypeface Bold { get; }

    public SKTypeface Italic { get; }

    public SKTypeface BoldItalic { get; }

    public SKTypeface Mono { get; }

    public SKTypeface MonoBold { get; }

    /// <summary>The typeface for a combination of weight, slant and code-ness.</summary>
    public SKTypeface For(bool bold, bool italic, bool mono) => (bold, italic, mono) switch
    {
        (_, _, true) when bold => MonoBold,
        (_, _, true) => Mono,
        (true, true, _) => BoldItalic,
        (true, false, _) => Bold,
        (false, true, _) => Italic,
        _ => Regular,
    };

    public void Dispose()
    {
        foreach (SKTypeface typeface in _owned)
        {
            typeface.Dispose();
        }

        _owned.Clear();
    }

    private SKTypeface Resolve(string[] families, SKFontStyle style)
    {
        foreach (string family in families)
        {
            SKTypeface? typeface = SKTypeface.FromFamilyName(family, style);

            // Skia hands back the default face rather than null for a family it does not
            // have, so the name is checked rather than the reference.
            if (typeface is not null
                && typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                _owned.Add(typeface);

                return typeface;
            }

            typeface?.Dispose();
        }

        SKTypeface fallback = SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;

        _owned.Add(fallback);

        return fallback;
    }
}
