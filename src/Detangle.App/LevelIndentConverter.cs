using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Detangle.App;

/// <summary>
/// Indents an outline entry by its heading level, so the right rail shows the shape of a
/// document rather than a flat list of its headings.
/// </summary>
public sealed class LevelIndentConverter : IValueConverter
{
    /// <summary>The shared instance bound to from XAML.</summary>
    public static LevelIndentConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int level ? Math.Clamp(level - 1, 0, 5) * 12 : 0, 0, 0, 0);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Outline indentation is display-only.");
}
