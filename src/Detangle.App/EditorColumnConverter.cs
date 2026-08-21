using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Detangle.App;

/// <summary>
/// Gives the split editor's column a width only while the editor is open.
/// <para>
/// A collapsed control is not measured, but a star-sized column still claims its share of
/// the grid regardless — so the closed editor was silently holding half the reading pane
/// and squeezing the document into the right-hand quarter of the window. The column has
/// to collapse, not just its contents.
/// </para>
/// </summary>
public sealed class EditorColumnConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static EditorColumnConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("The editor column is driven by the shell, not by the grid.");
}
