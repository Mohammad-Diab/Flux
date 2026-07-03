using System.Globalization;

namespace FluxRead.Converters;

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
{
 return value is bool b && !b;
    }
}

public class EnumToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
 return false;

        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
 if (value is bool isChecked && isChecked && parameter != null)
 {
     return Enum.Parse(targetType, parameter.ToString()!);
 }

        return Binding.DoNothing;
 }
}
