using Avalonia.Media;
using Detangle.Rendering.Highlighting;

namespace Detangle.Rendering.Controls;

/// <summary>
/// Colours and metrics for the reader. Held as a value rather than as XAML resources so
/// the renderer can be constructed in a test, and so a theme switch is a re-render with
/// a different value rather than a resource-dictionary swap.
/// </summary>
public sealed record DocumentTheme
{
    /// <summary>The light palette.</summary>
    public static DocumentTheme Light { get; } = new()
    {
        Highlighting = HighlightTheme.Light,
        Foreground = Brush.Parse("#12151b"),
        Muted = Brush.Parse("#586173"),
        Background = Brush.Parse("#ffffff"),
        SurfaceBackground = Brush.Parse("#f1f3f6"),
        Border = Brush.Parse("#dfe3ea"),
        Link = Brush.Parse("#1f6feb"),
        UnresolvedLink = Brush.Parse("#c0392b"),
        AmbiguousLink = Brush.Parse("#9a6700"),
        HighlightBackground = Brush.Parse("#faf0dc"),
        CodeBackground = Brush.Parse("#f5f7fa"),
    };

    /// <summary>The dark palette.</summary>
    public static DocumentTheme Dark { get; } = new()
    {
        Highlighting = HighlightTheme.Dark,
        Foreground = Brush.Parse("#e7eaf0"),
        Muted = Brush.Parse("#8b94a5"),
        Background = Brush.Parse("#0b0d12"),
        SurfaceBackground = Brush.Parse("#161b23"),
        Border = Brush.Parse("#242b36"),
        Link = Brush.Parse("#6cb6ff"),
        UnresolvedLink = Brush.Parse("#f2635a"),
        AmbiguousLink = Brush.Parse("#e0a33a"),
        HighlightBackground = Brush.Parse("#3a2e18"),
        CodeBackground = Brush.Parse("#11151c"),
    };

    /// <summary>Which TextMate theme fenced code is highlighted against.</summary>
    public required HighlightTheme Highlighting { get; init; }

    /// <summary>Body text.</summary>
    public required IBrush Foreground { get; init; }

    /// <summary>Secondary text: chips, captions, provenance.</summary>
    public required IBrush Muted { get; init; }

    /// <summary>The page background.</summary>
    public required IBrush Background { get; init; }

    /// <summary>Callout, table-header and properties-card background.</summary>
    public required IBrush SurfaceBackground { get; init; }

    /// <summary>Rules, table gridlines, card borders.</summary>
    public required IBrush Border { get; init; }

    /// <summary>A link that resolved.</summary>
    public required IBrush Link { get; init; }

    /// <summary>A link that reached step 13 without a target.</summary>
    public required IBrush UnresolvedLink { get; init; }

    /// <summary>A link that matched more than one document.</summary>
    public required IBrush AmbiguousLink { get; init; }

    /// <summary>"==highlighted==" text.</summary>
    public required IBrush HighlightBackground { get; init; }

    /// <summary>Fenced and inline code background.</summary>
    public required IBrush CodeBackground { get; init; }

    /// <summary>Body font size in device-independent pixels.</summary>
    public double FontSize { get; init; } = 15;

    /// <summary>
    /// Body face. Named rather than left as the default: the WebAssembly build has no
    /// system fonts to fall back through, and the default resolved to a monospace face
    /// there, which set every page of prose in code type.
    /// </summary>
    public FontFamily FontFamily { get; init; } = Fonts.Body;

    /// <summary>Monospace family for code and for identifiers.</summary>
    public FontFamily CodeFontFamily { get; init; } = Fonts.Mono;

    /// <summary>
    /// The face mathematics is set in. A separate family because the text faces that ship
    /// with desktop systems are missing the symbols: Segoe UI, Arial and Cascadia all lack
    /// the transpose sign, the tensor product and the angle brackets, and draw an empty box
    /// in their place.
    /// </summary>
    public FontFamily MathFontFamily { get; init; } = Fonts.Math;

    /// <summary>Per-callout accent colours, keyed by kind.</summary>
    public IReadOnlyDictionary<string, string> CalloutAccents { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["note"] = "#4c8dff",
            ["info"] = "#4c8dff",
            ["abstract"] = "#00b8d4",
            ["summary"] = "#00b8d4",
            ["tip"] = "#00bfa5",
            ["hint"] = "#00bfa5",
            ["success"] = "#2eb872",
            ["check"] = "#2eb872",
            ["question"] = "#c9a227",
            ["faq"] = "#c9a227",
            ["warning"] = "#e08c1a",
            ["caution"] = "#e08c1a",
            ["attention"] = "#e08c1a",
            ["failure"] = "#e5484d",
            ["danger"] = "#e5484d",
            ["error"] = "#e5484d",
            ["bug"] = "#e5484d",
            ["example"] = "#9d5cff",
            ["quote"] = "#8b949e",
            ["cite"] = "#8b949e",
        };

    /// <summary>The accent for a callout kind, falling back to the note colour.</summary>
    public IBrush AccentFor(string kind) =>
        Brush.Parse(CalloutAccents.TryGetValue(kind, out string? accent) ? accent : CalloutAccents["note"]);

    /// <summary>Heading size for a level, largest at level 1.</summary>
    public double HeadingSizeFor(int level) => level switch
    {
        1 => FontSize * 1.9,
        2 => FontSize * 1.5,
        3 => FontSize * 1.25,
        4 => FontSize * 1.1,
        _ => FontSize,
    };
}
