using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chapi.Presentation.Converters
{
    public class IsNotDefaultBranchConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string branchName)
            {
                // Verifica si es master o main, ya sea local ("master") o remoto ("origin/master")
                bool isDefault = branchName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
                                 branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                                 branchName.EndsWith("/master", StringComparison.OrdinalIgnoreCase) ||
                                 branchName.EndsWith("/main", StringComparison.OrdinalIgnoreCase);

                // Si NO es default, Visible. Si ES default, Collapsed.
                return !isDefault ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

