using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chapi.Presentation.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            if (value is string s) isNull = string.IsNullOrWhiteSpace(s);

            bool invert = parameter?.ToString() == "Inverted";

            if (invert)
                return isNull ? Visibility.Visible : Visibility.Collapsed;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
