namespace Detangle.Rendering.Highlighting;

/// <summary>One highlighted span of code.</summary>
/// <param name="Text">The span's text.</param>
/// <param name="Foreground">"#rrggbb" for the span, or null to use the default colour.</param>
/// <param name="IsBold">True when the theme asks for bold.</param>
/// <param name="IsItalic">True when the theme asks for italic.</param>
public sealed record HighlightSpan(string Text, string? Foreground, bool IsBold, bool IsItalic);

/// <summary>Which palette code is highlighted against.</summary>
public enum HighlightTheme
{
    /// <summary>Light editor theme.</summary>
    Light,

    /// <summary>Dark editor theme.</summary>
    Dark,
}

/// <summary>
/// Turns fenced code into spans.
/// <para>
/// Highlighters produce plain spans rather than controls: highlighting is a text
/// transformation, and keeping it Avalonia-free means the grammar mapping can be
/// asserted on directly instead of through a rendered visual tree.
/// </para>
/// </summary>
public interface ICodeHighlighter
{
    /// <summary>
    /// True when this highlighter can colour the fence's language. Nothing on the render
    /// path asks: <see cref="Highlight"/> already returns one plain span for a language it
    /// does not know, so asking first would only load a grammar to decide not to use it.
    /// This is here for diagnostics and for tests that want the answer without the spans.
    /// </summary>
    bool CanHighlight(string language);

    /// <summary>
    /// Highlights source, one list of spans per line. A language with no grammar — or an
    /// unlabelled fence — comes back as a single unstyled span per line rather than as an
    /// error, because a fence Detangle cannot colour must still be readable.
    /// </summary>
    IReadOnlyList<IReadOnlyList<HighlightSpan>> Highlight(string language, string source);
}

/// <summary>
/// The highlighter every head has: none. It returns each line as one unstyled span, which
/// the renderer draws in the body colour.
/// <para>
/// This is not a new behaviour invented for the WebAssembly demo. It is the branch the
/// TextMate highlighter has always taken for a language it has no grammar for, lifted out
/// so a head that ships no grammars at all lands in exactly the same place.
/// </para>
/// </summary>
public sealed class PlainCodeHighlighter : ICodeHighlighter
{
    /// <summary>The one instance; it holds nothing.</summary>
    public static PlainCodeHighlighter Instance { get; } = new();

    /// <inheritdoc />
    public bool CanHighlight(string language) => false;

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<HighlightSpan>> Highlight(string language, string source) =>
        [.. SplitLines(source).Select(line => (IReadOnlyList<HighlightSpan>)
            [new HighlightSpan(line, null, IsBold: false, IsItalic: false)])];

    /// <summary>
    /// Splits source into lines. Shared with the grammar-backed highlighter so the two
    /// cannot disagree about what a line is — a fence that changes line count depending on
    /// which head rendered it would be a bug nobody would think to look for.
    /// </summary>
    public static string[] SplitLines(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}

/// <summary>
/// Where a head says which highlighter it brought.
/// <para>
/// This is process-global on purpose. A renderer is built fresh for every page, deep
/// inside the view; threading a highlighter down to it would put a deployment decision
/// into every signature on the way. A head installs one at startup or does not, and the
/// difference is a colour rather than a failure.
/// </para>
/// </summary>
public static class CodeHighlighting
{
    /// <summary>Set by the head at startup. Null means no grammars were shipped.</summary>
    public static Func<HighlightTheme, ICodeHighlighter>? Provider { get; set; }

    /// <summary>The highlighter for a theme, or the plain fallback.</summary>
    public static ICodeHighlighter For(HighlightTheme theme) =>
        Provider?.Invoke(theme) ?? PlainCodeHighlighter.Instance;
}
