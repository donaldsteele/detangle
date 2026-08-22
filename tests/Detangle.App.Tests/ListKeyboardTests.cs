using Avalonia.Controls;
using Avalonia.Input;
using Detangle.App;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the command palette and search-result keyboard path (plan.md section 14.2).
/// <para>
/// Both lists used to run an entry from SelectionChanged, so moving the highlight ran
/// whatever it touched and there was no way to reach the second result without a mouse.
/// What these pin is the split: arrows highlight, Enter runs, and the two never happen
/// at once.
/// </para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ListKeyboardTests(HeadlessAppFixture app)
{
    [Fact]
    public void ArrowKeysMoveTheHighlightWithoutRunningAnything()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf("first", "second", "third");
            int runs = 0;

            Press(list, Key.Down, () => runs++);
            Assert.Equal(0, list.SelectedIndex);

            Press(list, Key.Down, () => runs++);
            Assert.Equal(1, list.SelectedIndex);

            Press(list, Key.Up, () => runs++);
            Assert.Equal(0, list.SelectedIndex);

            // The whole defect in one assertion: three keystrokes, nothing invoked.
            Assert.Equal(0, runs);
        });
    }

    [Fact]
    public void TheHighlightWrapsAtBothEnds()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf("first", "second");

            // Up from nothing selected reaches the last entry rather than doing nothing,
            // which is how a result list opened by keyboard gets to its bottom.
            Press(list, Key.Up, () => { });
            Assert.Equal(1, list.SelectedIndex);

            Press(list, Key.Down, () => { });
            Assert.Equal(0, list.SelectedIndex);
        });
    }

    [Fact]
    public void EnterRunsTheHighlightedEntry()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf("first", "second", "third");
            int? ran = null;

            Press(list, Key.Down, () => ran = list.SelectedIndex);
            Press(list, Key.Down, () => ran = list.SelectedIndex);

            KeyEventArgs enter = Press(list, Key.Enter, () => ran = list.SelectedIndex);

            Assert.Equal(1, ran);

            // Handled, or the window-wide shortcut handler gets the same keystroke.
            Assert.True(enter.Handled);
        });
    }

    [Fact]
    public void EnterWithNothingHighlightedRunsTheTopMatch()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf("first", "second");
            int? ran = null;

            Assert.Equal(-1, list.SelectedIndex);

            Press(list, Key.Enter, () => ran = list.SelectedIndex);

            Assert.Equal(0, ran);
        });
    }

    [Fact]
    public void AnEmptyListSwallowsNothing()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf();
            int runs = 0;

            KeyEventArgs args = Press(list, Key.Enter, () => runs++);

            Assert.Equal(0, runs);

            // Escape closes the palette from the window handler, and Enter on an empty
            // palette must not be marked handled or the shortcut layer never sees it.
            Assert.False(args.Handled);
        });
    }

    [Fact]
    public void RefreshingTheResultsPutsTheHighlightBackOnTheTopMatch()
    {
        app.Invoke(() =>
        {
            ListBox list = ListOf("first", "second");

            ListKeyboard.HighlightTop(list);
            Assert.Equal(0, list.SelectedIndex);

            // An existing highlight is left where the reader put it.
            list.SelectedIndex = 1;
            ListKeyboard.HighlightTop(list);
            Assert.Equal(1, list.SelectedIndex);
        });
    }

    private static ListBox ListOf(params string[] items) =>
        new() { ItemsSource = items };

    private static KeyEventArgs Press(ListBox list, Key key, Action commit)
    {
        var args = new KeyEventArgs { Key = key, RoutedEvent = InputElement.KeyDownEvent };

        ListKeyboard.OnKey(list, commit, args);

        return args;
    }
}
