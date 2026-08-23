using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Detangle.App;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for find-in-page. It searches the rendered control tree rather than the markdown,
/// so these build the same shape the renderer builds — text blocks of runs — and search
/// that.
/// </summary>
[Collection(HeadlessCollection.Name)]
public class FindInPageTests(HeadlessAppFixture app)
{
    [Fact]
    public void MatchesAreFoundInDocumentOrderAndCounted()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();

            find.Search(Page("the first line", "the second line"), "line", matchCase: false, Paint, Paint);

            Assert.Equal(2, find.Matches.Count);
            Assert.Equal(0, find.Current);
            Assert.Equal("1 of 2", find.Summary);
        });
    }

    [Fact]
    public void SteppingWrapsInBothDirections()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();

            find.Search(Page("a a a"), "a", matchCase: false, Paint, Paint);

            Assert.Equal(3, find.Matches.Count);

            find.Step(forward: true);
            find.Step(forward: true);
            Assert.Equal("3 of 3", find.Summary);

            find.Step(forward: true);
            Assert.Equal("1 of 3", find.Summary);

            find.Step(forward: false);
            Assert.Equal("3 of 3", find.Summary);
        });
    }

    [Fact]
    public void NothingFoundSaysSoRatherThanCountingToZero()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();

            find.Search(Page("nothing here"), "absent", matchCase: false, Paint, Paint);

            Assert.Empty(find.Matches);
            Assert.Equal("No matches", find.Summary);
            Assert.Null(find.CurrentMatch);
            Assert.Null(find.Step(forward: true));
        });
    }

    [Fact]
    public void CaseIsIgnoredUntilItIsAskedFor()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();

            find.Search(Page("Transformer and transformer"), "Transformer", matchCase: false, Paint, Paint);
            Assert.Equal(2, find.Matches.Count);

            find.Search(Page("Transformer and transformer"), "Transformer", matchCase: true, Paint, Paint);
            Assert.Single(find.Matches);
        });
    }

    [Fact]
    public void ClearingPutsEveryRunBackTheWayItWasFound()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();
            Control page = Page("one one two");

            IReadOnlyList<IBrush?> before = Backgrounds(page);

            // Two matches in one run: the run must be recorded once, or clearing restores
            // it to the highlight the first match applied rather than to what it was.
            find.Search(page, "one", matchCase: false, Paint, Paint);
            Assert.Equal(2, find.Matches.Count);

            find.Clear();

            Assert.Equal(before, Backgrounds(page));
            Assert.Empty(find.Matches);
            Assert.Equal(-1, find.Current);
        });
    }

    [Fact]
    public void SearchingAgainDoesNotLeaveTheLastSearchPainted()
    {
        app.Invoke(() =>
        {
            var find = new FindInPage();
            Control page = Page("alpha beta");

            IReadOnlyList<IBrush?> before = Backgrounds(page);

            find.Search(page, "alpha", matchCase: false, Paint, Paint);
            find.Search(page, string.Empty, matchCase: false, Paint, Paint);

            Assert.Equal(before, Backgrounds(page));
        });
    }

    private static IBrush Paint { get; } = Brushes.Yellow;

    /// <summary>A page shaped the way the renderer builds one: blocks of runs.</summary>
    private static Control Page(params string[] paragraphs)
    {
        var panel = new StackPanel();

        foreach (string paragraph in paragraphs)
        {
            var block = new SelectableTextBlock();

            // One run per word, which is roughly what an inline-styled paragraph produces
            // and is what makes the shared-run case above reachable.
            foreach (string word in paragraph.Split(' '))
            {
                block.Inlines!.Add(new Run(word + " "));
            }

            panel.Children.Add(block);
        }

        return panel;
    }

    private static IReadOnlyList<IBrush?> Backgrounds(Control page) =>
    [
        .. page.GetLogicalDescendants()
            .OfType<TextBlock>()
            .SelectMany(b => b.Inlines?.OfType<Run>() ?? [])
            .Select(r => r.Background),
    ];
}
