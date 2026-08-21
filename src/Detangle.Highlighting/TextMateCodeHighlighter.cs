using Detangle.Rendering.Highlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Detangle.Highlighting;

/// <summary>
/// Highlights fenced code with TextMateSharp, using the same VS Code grammars and themes
/// the rest of the world's tooling uses (plan.md section 11).
/// <para>
/// It lives in its own assembly rather than in Detangle.Rendering because the grammars
/// are 6.7 MB of embedded resources that no trimmer can reach. A head that opens unknown
/// vaults wants all of them; the WebAssembly demo, which ships one fixed wiki, would be
/// paying a tenth of its download for a set it never opens.
/// </para>
/// </summary>
public sealed class TextMateCodeHighlighter : ICodeHighlighter
{
    private readonly RegistryOptions _options;
    private readonly Registry _registry;
    private readonly Theme _theme;
    private readonly Dictionary<string, IGrammar?> _grammars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Creates a highlighter for one theme.</summary>
    public TextMateCodeHighlighter(HighlightTheme theme = HighlightTheme.Dark)
    {
        _options = new RegistryOptions(theme == HighlightTheme.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
    }

    /// <summary>
    /// Makes this the highlighter the renderer reaches for. A head that references this
    /// assembly must call it before anything can build a renderer; a head that does not
    /// reference the assembly gets plain fences, which is the point.
    /// </summary>
    public static void Install() =>
        CodeHighlighting.Provider = static theme => new TextMateCodeHighlighter(theme);

    /// <inheritdoc />
    public bool CanHighlight(string language) => ResolveGrammar(language) is not null;

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<HighlightSpan>> Highlight(string language, string source)
    {
        IGrammar? grammar = ResolveGrammar(language);

        if (grammar is null)
        {
            return PlainCodeHighlighter.Instance.Highlight(language, source);
        }

        string[] lines = PlainCodeHighlighter.SplitLines(source);
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
