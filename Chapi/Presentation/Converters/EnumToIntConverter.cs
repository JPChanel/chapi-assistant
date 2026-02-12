using System.Globalization;
using System.Windows.Data;

namespace Chapi.Presentation.Converters;

public class EnumToIntConverter : IValueConverter
{
    public static readonly EnumToIntConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Enum)
            return (int)value;
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i)
            return Enum.ToObject(targetType, i);
        return value;
    }
}
