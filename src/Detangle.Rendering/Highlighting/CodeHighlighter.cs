using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

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
/// Highlights fenced code with TextMateSharp, using the same VS Code grammars and themes
/// the rest of the world's tooling uses (plan.md section 11).
/// <para>
/// It produces plain spans rather than controls: highlighting is a text transformation,
/// and keeping it Avalonia-free means the grammar mapping can be asserted on directly
/// instead of through a rendered visual tree.
/// </para>
/// </summary>
public sealed class CodeHighlighter
{
    private readonly RegistryOptions _options;
    private readonly Registry _registry;
    private readonly Theme _theme;
    private readonly Dictionary<string, IGrammar?> _grammars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Creates a highlighter for one theme.</summary>
    public CodeHighlighter(HighlightTheme theme = HighlightTheme.Dark)
    {
        _options = new RegistryOptions(theme == HighlightTheme.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
    }

    /// <summary>True when a grammar is available for the fence's language.</summary>
    public bool CanHighlight(string language) => ResolveGrammar(language) is not null;

    /// <summary>
    /// Highlights source, one list of spans per line. A language with no grammar — or an
    /// unlabelled fence — comes back as a single unstyled span per line rather than as an
    /// error, because a fence Detangle cannot colour must still be readable.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<HighlightSpan>> Highlight(string language, string source)
    {
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        IGrammar? grammar = ResolveGrammar(language);

        if (grammar is null)
        {
            return [.. lines.Select(line => (IReadOnlyList<HighlightSpan>)
                [new HighlightSpan(line, null, IsBold: false, IsItalic: false)])];
        }

        var highlighted = new List<IReadOnlyList<HighlightSpan>>(lines.Length);
        IStateStack? state = null;

        foreach (string line in lines)
        {
            ITokenizeLineResult result = grammar.TokenizeLine(line, state, TimeSpan.FromSeconds(1));
            state = result.RuleStack;

            highlighted.Add(BuildSpans(line, result));
        }

        return highlighted;
    }

    private List<HighlightSpan> BuildSpans(string line, ITokenizeLineResult result)
    {
        var spans = new List<HighlightSpan>();

        foreach (IToken token in result.Tokens)
        {
            int start = Math.Clamp(token.StartIndex, 0, line.Length);
            int end = Math.Clamp(token.EndIndex, start, line.Length);

            if (end == start)
            {
                continue;
            }

            string text = line[start..end];
            string? foreground = null;
            bool bold = false;
            bool italic = false;

            foreach (ThemeTrieElementRule rule in _theme.Match(token.Scopes))
            {
                // The first rule that names a colour wins; later ones are less specific.
                if (foreground is null && rule.foreground > 0)
                {
                    foreground = ColorOf(rule.foreground);
                }

                bold |= (rule.fontStyle & FontStyle.Bold) != 0;
                italic |= (rule.fontStyle & FontStyle.Italic) != 0;
            }

            spans.Add(new HighlightSpan(text, foreground, bold, italic));
        }

        return spans.Count > 0
            ? spans
            : [new HighlightSpan(line, null, IsBold: false, IsItalic: false)];
    }

    /// <summary>
    /// Finds the grammar for a fence's info string. Grammars are cached — including the
    /// misses, since a vault full of "```text" fences would otherwise pay for the same
    /// failed lookup on every block.
    /// </summary>
    private IGrammar? ResolveGrammar(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        lock (_gate)
        {
            if (_grammars.TryGetValue(language, out IGrammar? cached))
            {
                return cached;
            }

            IGrammar? grammar = null;

            try
            {
                string? scope = _options.GetScopeByLanguageId(_options.GetLanguageByExtension($".{language}")?.Id ?? language);

                if (scope is not null)
                {
                    grammar = _registry.LoadGrammar(scope);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NullReferenceException)
            {
                grammar = null;
            }

            _grammars[language] = grammar;

            return grammar;
        }
    }

    private string? ColorOf(int id) => id <= 0 ? null : _theme.GetColor(id);
}
