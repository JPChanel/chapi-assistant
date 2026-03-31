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
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF3")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"))),
        AlertVariant.Warning => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF7ED")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F97316")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A3412"))),
        AlertVariant.Error => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"))),
        _ => new AlertPalette(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0C4A6E")))
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
