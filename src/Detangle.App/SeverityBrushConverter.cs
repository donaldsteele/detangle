using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Detangle.Core.Diagnostics;

namespace Detangle.App;

/// <summary>
/// The colour of a Link Doctor finding's severity marker.
/// <para>
/// A three-pixel bar rather than a coloured row: the list is read by scanning, and the
/// eye needs a rank, not a highlight. The hues are the same ones the link ledger uses, so
/// red means the same thing everywhere in the application.
/// </para>
/// </summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static SeverityBrushConverter Instance { get; } = new();

    private static readonly SolidColorBrush Error = new(Color.FromRgb(0xF2, 0x63, 0x5A));
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xD9, 0x9A, 0x2B));
    private static readonly SolidColorBrush Info = new(Color.FromRgb(0x5B, 0x64, 0x74));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            FindingSeverity.Error => Error,
            FindingSeverity.Warning => Warning,
            _ => Info,
        };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Severity colours are read-only.");
}
