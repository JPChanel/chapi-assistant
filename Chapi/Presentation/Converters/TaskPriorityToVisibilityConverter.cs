using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Chapi.Domain.Enums;

namespace Chapi.Presentation.Converters;



public class TaskPriorityToVisibilityConverter : IValueConverter
{
    private readonly TaskPriority _target;

    public TaskPriorityToVisibilityConverter(TaskPriority target)
    {
        _target = target;
    }

    public static readonly IValueConverter Alta = new TaskPriorityToVisibilityConverter(TaskPriority.Alta);
    public static readonly IValueConverter Media = new TaskPriorityToVisibilityConverter(TaskPriority.Media);
    public static readonly IValueConverter Baja = new TaskPriorityToVisibilityConverter(TaskPriority.Baja);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TaskPriority priority)
        {
            return priority == _target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
