using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Detangle.Rendering.Controls;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Regression tests for the measure-pass hang.
/// <para>
/// Avalonia 12.1.1's text layout does not terminate for a block whose text contains an
/// empty line when it is measured with unbounded height — which is what every vertical
/// StackPanel hands its children. One blank line in one caption was enough to hang a
/// whole page. <see cref="DocumentRenderer.Wrappable"/> folds every string bound for a
/// text block to a single line, and multi-line source is laid out one control per line.
/// </para>
/// <para>
/// These tests only assert that things finish. Asserting that the unguarded form hangs
/// would mean abandoning a measure mid-flight, and the UI thread never recovers from
/// that — one such test times out every test that follows it.
/// </para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class LayoutProbeTests
{
    private const string BlankLineText = "first paragraph\n\nsecond paragraph";

    [Fact]
    public void TheGuardFoldsTextToOneLine()
    {
        Assert.Equal("first paragraph second paragraph", DocumentRenderer.Wrappable(BlankLineText));
        Assert.Equal("a b", DocumentRenderer.Wrappable("a\n   \n\t\nb"));
        Assert.Equal("a b", DocumentRenderer.Wrappable("a\r\nb"));
        Assert.Equal(string.Empty, DocumentRenderer.Wrappable(null));
    }

    [Fact]
    public void TheGuardLeavesSingleLineTextExactlyAsItWas()
    {
        // Leading and trailing spaces carry meaning in code spans and captions.
        Assert.Equal("  spaced  ", DocumentRenderer.Wrappable("  spaced  "));
    }

    [Fact]
    public async Task GuardedTextMeasures()
    {
        bool finished = await MeasuresWithin(() => new SelectableTextBlock
        {
            Text = DocumentRenderer.Wrappable(BlankLineText),
            TextWrapping = TextWrapping.Wrap,
        });

        Assert.True(finished);
    }

    [Fact]
    public async Task OneControlPerLineMeasures()
    {
        // The route multi-line source takes: no control ever holds a newline.
        bool finished = await MeasuresWithin(() =>
        {
            var panel = new StackPanel();

            foreach (string line in BlankLineText.ReplaceLineEndings("\n").Split('\n'))
            {
                panel.Children.Add(new SelectableTextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.NoWrap,
                });
            }

            return panel;
        });

        Assert.True(finished);
    }

    [Theory]
    [InlineData("A paragraph, then a blank line, then more.\n\nAnd more text here.\n")]
    [InlineData("```dbml\nTable a {\n  id int [pk]\n}\n\nTable b {\n  id int [pk]\n}\n```")]
    [InlineData("```csharp\nvar a = 1;\n\nvar b = 2;\n```")]
    [InlineData("$$\nx = 1\n\ny = 2\n$$")]
    [InlineData("> [!note] A note\n> Body text.\n")]
    [InlineData("??? tip \"Folded\"\n    Hidden body.\n")]
    public async Task DocumentsThatOnceHungNowRender(string markdown)
    {
        RenderDocument rendered = RenderTestVault.Build(("page.md", markdown)).Render("page.md");

        bool finished = await MeasuresWithin(() => new DocumentRenderer(DocumentTheme.Light).Render(rendered));

        Assert.True(finished);
    }

    [Fact]
    public async Task PropertiesCardsSurviveMultiLineFrontmatterValues()
    {
        // A YAML block scalar arrives with its blank lines intact.
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "---\ntitle: A Page\nnote: |\n  first\n\n  second\n---\n\n# Body\n"))
            .Render("page.md");

        bool finished = await MeasuresWithin(() => new DocumentRenderer(DocumentTheme.Light).Render(rendered));

        Assert.True(finished);
    }

    [Fact]
    public async Task PropertiesCardsWithChipsAndLinksMeasure()
    {
        RenderDocument rendered = RenderTestVault.Build(
            ("page.md", "---\ntitle: A Page\ntags: [alpha, beta]\naliases: [Another Name]\n"
                + "related:\n  - other\n---\n\n# Body\n"),
            ("other.md", "# Other")).Render("page.md");

        bool finished = await MeasuresWithin(() => new DocumentRenderer(DocumentTheme.Light).Render(rendered));

        Assert.True(finished);
    }

    [Fact]
    public async Task TheWholeReaderFixtureMeasures()
    {
        // reader.md is the page that found this bug: callouts, tables, code, diagrams,
        // math, definitions, footnotes, transclusions and attachments on one page.
        Core.Vault.VaultSnapshot vault = Core.Vault.VaultScanner.Scan(FindVault("torture"));

        RenderDocument rendered = new RenderModelBuilder(vault)
            .Build(vault.Index.ByRelativePath("reader.md").Single());

        bool finished = await MeasuresWithin(() => new DocumentRenderer(DocumentTheme.Light).Render(rendered));

        Assert.True(finished);
    }

    /// <summary>Builds and measures a control on the UI thread, with a watchdog.</summary>
    private static async Task<bool> MeasuresWithin(Func<Control> factory, Size? available = null)
    {
        Task measure = Dispatcher.UIThread.InvokeAsync(() =>
        {
            Control control = factory();

            control.Measure(available ?? new Size(900, double.PositiveInfinity));
            control.Arrange(new Rect(control.DesiredSize));
        }).GetTask();

        Task finished = await Task.WhenAny(
            measure,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        return finished == measure;
    }

    private static string FindVault(string vaultName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "vaults", vaultName);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(vaultName);
    }
}
