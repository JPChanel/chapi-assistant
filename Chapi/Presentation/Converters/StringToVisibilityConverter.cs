using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chapi.Presentation.Converters;

public class StringToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEmpty = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        bool doInverse = Inverse;

        if (parameter?.ToString() == "Invert")
            doInverse = !doInverse;
        
        if (doInverse)
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
            
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
