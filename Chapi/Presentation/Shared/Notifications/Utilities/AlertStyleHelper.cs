using Chapi.Presentation.Shared.Notifications.Models;
using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Media;

namespace Chapi.Presentation.Shared.Notifications.Utilities;

public static class AlertStyleHelper
{
    public static AlertPalette ResolvePalette(AlertVariant variant) => variant switch
    {
        AlertVariant.Success => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"))),
        AlertVariant.Warning => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"))),
        AlertVariant.Error => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"))),
        _ => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EEF2FF")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#312E81")))
    };

    public static PackIconKind GetDefaultIcon(AlertVariant variant) => variant switch
    {
        AlertVariant.Success => PackIconKind.CheckCircleOutline,
        AlertVariant.Warning => PackIconKind.AlertOutline,
        AlertVariant.Error => PackIconKind.AlertCircleOutline,
        _ => PackIconKind.InformationOutline
    };

    public static PackIconKind ResolveIcon(AlertVariant variant, MessageBoxImage image)
    {
        if (image == MessageBoxImage.Error)
        {
            return PackIconKind.AlertCircleOutline;
        }

        if (image == MessageBoxImage.Warning)
        {
            return PackIconKind.AlertOutline;
        }

        if (image == MessageBoxImage.Question)
        {
            return PackIconKind.HelpCircleOutline;
        }

        if (image == MessageBoxImage.Information)
        {
            return PackIconKind.InformationOutline;
        }

        return GetDefaultIcon(variant);
    }
}
