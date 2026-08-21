using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Detangle.App;

/// <summary>
/// Gives a side panel's column its width only while the panel is showing.
/// <para>
/// The same trap as <see cref="EditorColumnConverter"/>, for the same reason: a collapsed
/// control is not measured, but a column with a fixed or star width still claims its share
/// of the grid. The navigation rail's column is <c>Auto</c> and does collapse on its own;
/// the outline panel's column is a fixed 300 and would otherwise leave a 300-pixel hole
/// where the panel used to be.
/// </para>
/// <para>
/// The width is a parameter so both rails can share one converter and keep their sizes in
/// the layout, where a reader of the XAML will look for them.
/// </para>
/// </summary>
public sealed class PanelColumnConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static PanelColumnConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return new GridLength(0);
        }

        return parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            ? new GridLength(width)
            : GridLength.Auto;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Panel columns are driven by the shell, not by the grid.");
}
