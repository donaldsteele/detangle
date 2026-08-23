using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Detangle.App;

/// <summary>One occurrence of the search text in the rendered page.</summary>
/// <param name="Owner">The text block it was found in, which is what gets scrolled to.</param>
/// <param name="Run">The run holding it.</param>
/// <param name="Start">Where in that run's text the match starts.</param>
/// <param name="Length">How long the match is.</param>
internal sealed record PageMatch(TextBlock Owner, Run Run, int Start, int Length);

/// <summary>
/// Find-in-page over the rendered document.
/// <para>
/// The search runs over the control tree rather than the markdown, so it finds what the
/// reader can actually see: the label of a wikilink rather than its target, a table cell
/// rather than its pipes, and nothing at all inside a frontmatter block that is drawn as a
/// property panel. Searching the source would report matches at places the reader cannot
/// be shown, which is worse than not searching.
/// </para>
/// <para>
/// That correctness rests on the reading pane not virtualising: every block of the open
/// page is a realised control. If long pages are ever virtualised, this has to move onto
/// the render model, because a find that silently skips matches below the fold is worse
/// than no find at all.
/// </para>
/// </summary>
internal sealed class FindInPage
{
    private readonly List<PageMatch> _matches = [];
    private readonly List<(Run Run, IBrush? Background, IBrush? Foreground)> _painted = [];

    private IBrush? _matchBrush;
    private IBrush? _currentBrush;

    /// <summary>What was found, in document order.</summary>
    public IReadOnlyList<PageMatch> Matches => _matches;

    /// <summary>Which match is current, or -1 when there are none.</summary>
    public int Current { get; private set; } = -1;

    /// <summary>What the counter beside the box says.</summary>
    public string Summary => _matches.Count == 0
        ? "No matches"
        : $"{Current + 1} of {_matches.Count}";

    /// <summary>
    /// Searches the rendered page and highlights every hit.
    /// </summary>
    /// <param name="root">The rendered document's root control.</param>
    /// <param name="query">What to look for; empty clears.</param>
    /// <param name="matchCase">True to distinguish case.</param>
    /// <param name="matchBrush">The paint for every hit.</param>
    /// <param name="currentBrush">The paint for the one the reader is on.</param>
    public void Search(
        Control? root, string query, bool matchCase, IBrush? matchBrush, IBrush? currentBrush)
    {
        Clear();

        _matchBrush = matchBrush;
        _currentBrush = currentBrush;

        if (root is null || query.Length == 0)
        {
            return;
        }

        StringComparison comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // Logical order rather than visual: the logical tree is the order the blocks were
        // built in, which is the order they are read in.
        foreach (TextBlock block in root.GetLogicalDescendants().OfType<TextBlock>())
        {
            foreach (Run run in block.Inlines?.OfType<Run>() ?? [])
            {
                string text = run.Text ?? string.Empty;
                int at = 0;

                while (at < text.Length
                    && text.IndexOf(query, at, comparison) is var found and >= 0)
                {
                    _matches.Add(new PageMatch(block, run, found, query.Length));
                    at = found + query.Length;
                }
            }
        }

        if (_matches.Count > 0)
        {
            Current = 0;
            Paint();
        }
    }

    /// <summary>Moves to the next or previous match, wrapping.</summary>
    /// <param name="forward">False to go backwards.</param>
    /// <returns>The match to scroll to, or null when there are none.</returns>
    public PageMatch? Step(bool forward)
    {
        if (_matches.Count == 0)
        {
            return null;
        }

        Current = (Current + (forward ? 1 : -1) + _matches.Count) % _matches.Count;

        Paint();

        return _matches[Current];
    }

    /// <summary>The match the reader is on, or null.</summary>
    public PageMatch? CurrentMatch => Current >= 0 && Current < _matches.Count ? _matches[Current] : null;

    /// <summary>Puts every painted run back the way it was found.</summary>
    public void Clear()
    {
        foreach ((Run run, IBrush? background, IBrush? foreground) in _painted)
        {
            run.Background = background;
            run.Foreground = foreground;
        }

        _painted.Clear();
        _matches.Clear();
        Current = -1;
    }

    /// <summary>
    /// Paints the hits.
    /// <para>
    /// A whole run at a time, not the matched characters: Avalonia has no cheap way to
    /// paint a range inside a run, and splitting every run into three would rebuild the
    /// page's inline collections on every keystroke. A run is usually a phrase, so this is
    /// close enough to point at the match, which is what the reader needs.
    /// </para>
    /// </summary>
    private void Paint()
    {
        foreach ((Run run, IBrush? background, IBrush? foreground) in _painted)
        {
            run.Background = background;
            run.Foreground = foreground;
        }

        _painted.Clear();

        // Two matches can share one run, so each run is recorded once — a second record
        // would capture the highlight the first just applied, and clearing would then
        // leave the page painted.
        foreach (Run run in _matches.Select(m => m.Run).Distinct())
        {
            _painted.Add((run, run.Background, run.Foreground));
            run.Background = _matchBrush;
        }

        // Then the one the reader is standing on, over the top. Where a run holds both, the
        // current match wins it, which is what the reader is looking for.
        if (CurrentMatch is { } current)
        {
            current.Run.Background = _currentBrush;
        }
    }
}
