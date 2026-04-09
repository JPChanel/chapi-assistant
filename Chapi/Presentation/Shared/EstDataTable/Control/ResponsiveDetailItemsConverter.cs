using System;
using System.Globalization;
using System.Windows.Data;

namespace app_desktop_base.Controls;

public sealed class ResponsiveDetailItemsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not EstDataTable table)
        {
            return Array.Empty<ResponsiveDetailItem>();
        }

        return values[0] is null
            ? Array.Empty<ResponsiveDetailItem>()
            : table.GetResponsiveDetailItems(values[0]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
