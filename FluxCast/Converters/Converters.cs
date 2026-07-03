using System.Globalization;

namespace FluxCast.Converters;

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
  return value is bool b ? !b : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
   return value is bool b ? !b : value;
    }
}

public class IsNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
}

public class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
     return value is not null;
    }

public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
}

public class ByteArrayToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
 if (value is byte[] bytes && bytes.Length > 0)
     {
    return ImageSource.FromStream(() => new MemoryStream(bytes));
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
 throw new NotImplementedException();
    }
}

public class PlayPauseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
     return value is bool isPlaying && isPlaying ? "Pause" : "Play";
    }

 public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
   throw new NotImplementedException();
    }
}
