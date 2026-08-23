using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Detangle.Core.Linking;

namespace Detangle.App;

/// <summary>
/// The link ledger: one hairline strip showing how the open page's links resolved.
/// <para>
/// Every other reader tells you a link is broken when you click it. Detangle knows before
/// you read a word, so this says so — a bar segmented by resolution class, in proportion.
/// A page that is entirely exact matches shows one calm green line; a page the generator
/// made a mess of shows red, and you know to open the Doctor before you trust it.
/// </para>
/// <para>
/// This is the only saturated colour in the application, which is what lets it mean
/// something. Hovering names the counts; the strip itself never moves or animates.
/// </para>
/// </summary>
public sealed class LinkLedger : Control
{
    /// <summary>The resolutions to summarise.</summary>
    public static readonly StyledProperty<IReadOnlyList<LinkResolution>?> ResolutionsProperty =
        AvaloniaProperty.Register<LinkLedger, IReadOnlyList<LinkResolution>?>(nameof(Resolutions));

    private static readonly Band[] Bands =
    [
        new(ResolutionConfidence.Exact, "Exact", "exact"),
        new(ResolutionConfidence.Normalized, "Normalized", "normalized"),
        new(ResolutionConfidence.Heuristic, "Heuristic", "heuristic"),
        new(ResolutionConfidence.Suggestion, "Ambiguous", "suggested"),
        new(ResolutionConfidence.Unresolved, "Broken", "unresolved"),
    ];

    static LinkLedger()
    {
        AffectsRender<LinkLedger>(ResolutionsProperty);
        AffectsMeasure<LinkLedger>(ResolutionsProperty);
    }

    /// <summary>How tall the painted strip is. The hit target is larger.</summary>
    private const double PaintHeight = 3;

    /// <summary>Creates a ledger.</summary>
    public LinkLedger()
    {
        // Ten pixels of target, three of paint. The strip is the one graphic idea in the
        // centre pane and must not get heavier, but a 3px click target is one nobody hits
        // and a 3px hover target is a tooltip nobody sees.
        Height = 10;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Raised when a band of the strip is clicked, carrying the resolution class under the
    /// pointer. The strip has always known which page's links are broken; this is what
    /// makes it say so.
    /// </summary>
    public event EventHandler<ResolutionConfidence>? SegmentClicked;

    /// <summary>The resolutions to summarise. Null or empty draws nothing.</summary>
    public IReadOnlyList<LinkResolution>? Resolutions
    {
        get => GetValue(ResolutionsProperty);
        set => SetValue(ResolutionsProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        int[] counts = Count();
        int total = counts.Sum();

        if (total == 0)
        {
            return;
        }

        // Painted against the bottom of the control, so growing the hit target upward did
        // not move the line: it still sits directly under the tab strip.
        double top = Bounds.Height - PaintHeight;

        foreach ((int index, double x, double span) in Segments(counts, total))
        {
            if (Brush(Bands[index].Key) is { } brush)
            {
                context.FillRectangle(brush, new Rect(x, top, span, PaintHeight));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (BandAt(e.GetPosition(this).X) is { } band)
        {
            SegmentClicked?.Invoke(this, band);
            e.Handled = true;
        }
    }

    /// <summary>Which resolution class is under a horizontal position, if any.</summary>
    private ResolutionConfidence? BandAt(double position)
    {
        int[] counts = Count();
        int total = counts.Sum();

        if (total == 0)
        {
            return null;
        }

        foreach ((int index, double x, double span) in Segments(counts, total))
        {
            if (position >= x && position < x + span)
            {
                return Bands[index].Confidence;
            }
        }

        return null;
    }

    /// <summary>Where each non-empty band sits along the strip.</summary>
    private IEnumerable<(int Index, double X, double Span)> Segments(int[] counts, int total)
    {
        double x = 0;
        double width = Bounds.Width;

        for (int i = 0; i < Bands.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            // The last segment takes whatever rounding left over, so the bar always
            // reaches the right edge and a page with no broken links never appears to
            // have a sliver of one.
            double span = i == LastNonEmpty(counts)
                ? width - x
                : Math.Round(width * counts[i] / total);

            yield return (i, x, span);

            x += span;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        int[] counts = Count();

        if (counts.Sum() == 0)
        {
            ToolTip.SetTip(this, null);
            return;
        }

        string summary = string.Join(
            " · ",
            Bands.Select((band, i) => (band, count: counts[i]))
                .Where(entry => entry.count > 0)
                .Select(entry => $"{entry.count} {entry.band.Label}"));

        ToolTip.SetTip(
            this,
            BandAt(e.GetPosition(this).X) is { } band
                ? $"{summary}\nClick to list this page's {Bands.First(b => b.Confidence == band).Label} links"
                : summary);
    }

    private int[] Count()
    {
        int[] counts = new int[Bands.Length];

        foreach (LinkResolution resolution in Resolutions ?? [])
        {
            // External links resolve to nothing in this vault by definition; counting
            // them as unresolved would make every page with a citation look broken.
            if (resolution.Link.IsExternal || resolution.Rule == ResolutionRule.NotAttempted)
            {
                continue;
            }

            int index = Array.FindIndex(Bands, band => band.Confidence == resolution.Confidence);

            if (index >= 0)
            {
                counts[index]++;
            }
        }

        return counts;
    }

    private static int LastNonEmpty(int[] counts)
    {
        for (int i = counts.Length - 1; i >= 0; i--)
        {
            if (counts[i] > 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The brush for a resolution class, taken from the theme dictionary by name so the
    /// ledger follows a palette switch without being told about it.
    /// </summary>
    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) ? value as IBrush : null;

    private sealed record Band(ResolutionConfidence Confidence, string Key, string Label);
}
