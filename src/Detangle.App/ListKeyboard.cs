using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Detangle.App;

/// <summary>
/// Gives a text box and the list under it a keyboard path: the box keeps focus and keeps
/// typing, the arrows move the highlight, and Enter runs what is highlighted.
/// <para>
/// The command palette and the search results both used to run an entry from
/// SelectionChanged, which makes arrow keys impossible — moving the highlight would run
/// the first entry it touched — so there was no way to reach the second result except
/// with the mouse (plan.md section 14.2). Highlighting and running have to be separate
/// events for a list to be navigable at all.
/// </para>
/// </summary>
internal static class ListKeyboard
{
    /// <summary>Wires one box to one list.</summary>
    /// <param name="box">The box that keeps focus and receives the keys.</param>
    /// <param name="list">The list the keys move through.</param>
    /// <param name="commit">Runs the highlighted entry.</param>
    public static void Wire(TextBox box, ListBox list, Action commit)
    {
        box.KeyDown += (_, args) => OnKey(list, commit, args);

        // Typing replaces the results, which clears the highlight. Putting it back on the
        // top match is what makes Enter mean "the one you are looking at". It is posted
        // rather than done here because the list has not been refreshed from the binding
        // yet when TextChanged runs.
        box.TextChanged += (_, _) => Dispatcher.UIThread.Post(() => HighlightTop(list), DispatcherPriority.Background);

        // A row is selected on pointer press, so by the time the button comes back up the
        // highlight is already the row under the pointer. The release has to have landed
        // on a row, though: the palette is a fixed-height panel with empty space under a
        // short result list, and a stray click there would otherwise run whatever was
        // highlighted.
        list.PointerReleased += (_, args) =>
        {
            if (args.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            {
                commit();
            }
        };
    }

    /// <summary>
    /// Handles one key against the list. Separate from <see cref="Wire"/> so it can be
    /// exercised without a focused window: synthesising a real key press needs one, and
    /// what is worth pinning here is the navigation, not Avalonia's input routing.
    /// </summary>
    public static void OnKey(ListBox list, Action commit, KeyEventArgs args)
    {
        int count = list.ItemCount;

        if (count == 0)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Down:
                Highlight((list.SelectedIndex + 1) % count);
                break;

            case Key.Up:
                Highlight((list.SelectedIndex <= 0 ? count : list.SelectedIndex) - 1);
                break;

            case Key.Enter:
                // Enter with nothing highlighted means the top match, which is what every
                // result list does and what the first keystroke after opening the palette
                // expects.
                HighlightTop(list);
                commit();
                args.Handled = true;
                break;
        }

        void Highlight(int index)
        {
            list.SelectedIndex = index;
            list.ScrollIntoView(index);
            args.Handled = true;
        }
    }

    /// <summary>Highlights the first entry when nothing is highlighted yet.</summary>
    public static void HighlightTop(ListBox list)
    {
        if (list.ItemCount > 0 && list.SelectedIndex < 0)
        {
            list.SelectedIndex = 0;
        }
    }
}
