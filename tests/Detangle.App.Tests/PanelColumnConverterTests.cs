using System.Globalization;
using Avalonia.Controls;
using Detangle.App;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for the side panels' column widths.
/// <para>
/// This exists because hiding a panel is not the same as reclaiming its space, and this
/// codebase has already shipped that mistake once: the closed split editor kept half the
/// reading pane because a star-sized column claims its share whether or not anything is in
/// it. A fixed-width column does the same. What has to be asserted is the width, not the
/// visibility.
/// </para>
/// </summary>
public class PanelColumnConverterTests
{
    [Fact]
    public void AHiddenPanelGivesUpItsColumnEntirely()
    {
        object width = PanelColumnConverter.Instance.Convert(
            false, typeof(GridLength), "300", CultureInfo.InvariantCulture);

        Assert.Equal(new GridLength(0), width);
    }

    [Fact]
    public void AShownPanelGetsTheWidthTheLayoutAsksFor()
    {
        object width = PanelColumnConverter.Instance.Convert(
            true, typeof(GridLength), "300", CultureInfo.InvariantCulture);

        Assert.Equal(new GridLength(300), width);
    }

    [Fact]
    public void AShownPanelWithNoStatedWidthSizesToItsContents()
    {
        object width = PanelColumnConverter.Instance.Convert(
            true, typeof(GridLength), null, CultureInfo.InvariantCulture);

        Assert.Equal(GridLength.Auto, width);
    }

    [Fact]
    public void TheWidthIsReadTheSameWayInEveryLocale()
    {
        // The parameter is XAML text, so it is invariant no matter what the machine's
        // culture separator is. Parsing it with the current culture would give a German
        // machine a 3-pixel panel for "300" the moment a decimal appeared.
        object width = PanelColumnConverter.Instance.Convert(
            true, typeof(GridLength), "300.5", new CultureInfo("de-DE"));

        Assert.Equal(new GridLength(300.5), width);
    }
}
