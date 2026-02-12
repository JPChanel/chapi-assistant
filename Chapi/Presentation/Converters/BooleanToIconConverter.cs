using System.Globalization;
using System.Windows.Data;

namespace Chapi.Presentation.Converters;

public class BooleanToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value is bool b && b;
        var icons = parameter?.ToString()?.Split(';');

        if (icons?.Length == 2)
        {
            return isTrue ? icons[0] : icons[1];
        }

        return isTrue ? "Check" : "Close";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
