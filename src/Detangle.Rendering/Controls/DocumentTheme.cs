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
        Foreground = Brush.Parse("#1a1d21"),
        Muted = Brush.Parse("#5c6570"),
        Background = Brush.Parse("#fdfdfc"),
        SurfaceBackground = Brush.Parse("#f3f3f1"),
        Border = Brush.Parse("#dcdcd8"),
        Link = Brush.Parse("#1f6feb"),
        UnresolvedLink = Brush.Parse("#b3261e"),
        AmbiguousLink = Brush.Parse("#9a6700"),
        HighlightBackground = Brush.Parse("#fff3b0"),
        CodeBackground = Brush.Parse("#f6f6f4"),
    };

    /// <summary>The dark palette.</summary>
    public static DocumentTheme Dark { get; } = new()
    {
        Highlighting = HighlightTheme.Dark,
        Foreground = Brush.Parse("#e6e6e3"),
        Muted = Brush.Parse("#9aa4b0"),
        Background = Brush.Parse("#16181b"),
        SurfaceBackground = Brush.Parse("#1e2125"),
        Border = Brush.Parse("#31363c"),
        Link = Brush.Parse("#6cb6ff"),
        UnresolvedLink = Brush.Parse("#ff7b72"),
        AmbiguousLink = Brush.Parse("#e3b341"),
        HighlightBackground = Brush.Parse("#4a4425"),
        CodeBackground = Brush.Parse("#1b1e22"),
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

    /// <summary>Body font family; falls back through the platform's default UI stack.</summary>
    public FontFamily FontFamily { get; init; } = FontFamily.Default;

    /// <summary>Monospace family for code.</summary>
    public FontFamily CodeFontFamily { get; init; } =
        new("Cascadia Mono, Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");

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
