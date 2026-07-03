using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluxCast.Converters;

/// <summary>
/// Collapses an element when the bound value is null (or an empty string).
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value as string == "" ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
