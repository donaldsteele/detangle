using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Detangle.App;

/// <summary>
/// Find in page: the most reflexive thing a reader does in a document viewer, and until
/// now the one the application had no answer for. A reader could search the whole vault
/// and not the page in front of them.
/// </summary>
public partial class ShellView
{
    private readonly FindInPage _find = new();

    /// <summary>Wires the find bar. Called from the constructor.</summary>
    private void WireFind()
    {
        FindBox.TextChanged += (_, _) => RunFind();
        FindMatchCaseBox.IsCheckedChanged += (_, _) => RunFind();
        FindNextButton.Click += (_, _) => StepFind(forward: true);
        FindPreviousButton.Click += (_, _) => StepFind(forward: false);
        FindCloseButton.Click += (_, _) => CloseFind();

        FindBox.KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case Key.Enter:
                    StepFind(forward: !args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    args.Handled = true;
                    break;

                case Key.Escape:
                    CloseFind();
                    args.Handled = true;
                    break;

                default:
                    break;
            }
        };
    }

    /// <summary>
    /// Opens the find bar, or hands Ctrl+F to the editor when the editor has focus.
    /// <para>
    /// AvaloniaEdit brings its own search panel, and Ctrl+F meaning two different things
    /// depending on where the caret is would be worse than either.
    /// </para>
    /// </summary>
    private void OpenFind()
    {
        if (Editor.IsKeyboardFocusWithin)
        {
            AvaloniaEdit.Search.SearchPanel.Install(Editor).Open();

            return;
        }

        FindBar.IsVisible = true;

        // Selected rather than cleared: pressing Ctrl+F again to search for something else
        // should not need the reader to delete what is there first.
        FindBox.SelectAll();
        FindBox.Focus();

        RunFind();
    }

    private void CloseFind()
    {
        FindBar.IsVisible = false;

        _find.Clear();

        // Back to the document, so the next Page Down scrolls the page rather than doing
        // nothing in a box that is no longer on screen.
        DocumentScroller.Focus();
    }

    private void RunFind()
    {
        if (!FindBar.IsVisible)
        {
            return;
        }

        _find.Search(
            DocumentHost.Content as Control,
            FindBox.Text ?? string.Empty,
            FindMatchCaseBox.IsChecked == true,
            Resource("Selection"),
            Resource("AccentSoft"));

        FindSummary.Text = (FindBox.Text ?? string.Empty).Length == 0 ? string.Empty : _find.Summary;

        ScrollTo(_find.CurrentMatch);
    }

    private void StepFind(bool forward)
    {
        ScrollTo(_find.Step(forward));

        FindSummary.Text = _find.Summary;
    }

    /// <summary>Brings a match into view, using the same path an anchor link scrolls by.</summary>
    private void ScrollTo(PageMatch? match)
    {
        if (match is null || DocumentHost.Content is not Control content
            || match.Owner.TranslatePoint(default, content) is not { } point)
        {
            return;
        }

        DocumentScroller.Offset = new Vector(
            0, Math.Max(0, point.Y - (DocumentScroller.Bounds.Height / 3)));
    }

    private IBrush? Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) ? value as IBrush : null;
}
