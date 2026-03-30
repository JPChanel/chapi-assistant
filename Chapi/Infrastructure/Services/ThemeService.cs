using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace Chapi.Infrastructure.Services;

public static class ThemeService
{
    public const string SystemMode = "System";
    public const string DarkMode = "Dark";
    public const string LightMode = "Light";

    public static string NormalizeThemeMode(string? raw)
    {
        if (string.Equals(raw, SystemMode, StringComparison.OrdinalIgnoreCase))
            return SystemMode;

        if (string.Equals(raw, LightMode, StringComparison.OrdinalIgnoreCase))
            return LightMode;

        return DarkMode;
    }

    public static void ApplyTheme(string? rawThemeMode)
    {
        var themeMode = NormalizeThemeMode(rawThemeMode);
        var effectiveThemeMode = ResolveEffectiveThemeMode(themeMode);
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();

        theme.SetBaseTheme(effectiveThemeMode == LightMode ? BaseTheme.Light : BaseTheme.Dark);
        paletteHelper.SetTheme(theme);
        ApplyHeaderTheme(effectiveThemeMode);
    }

    private static string ResolveEffectiveThemeMode(string themeMode)
    {
        if (!string.Equals(themeMode, SystemMode, StringComparison.OrdinalIgnoreCase))
            return themeMode;

        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsUseLightTheme = personalize?.GetValue("AppsUseLightTheme");

            if (appsUseLightTheme is int value)
                return value > 0 ? LightMode : DarkMode;
        }
        catch
        {
        }

        return DarkMode;
    }

    private static void ApplyHeaderTheme(string themeMode)
    {
        if (System.Windows.Application.Current?.Resources is not ResourceDictionary resources)
            return;

        var isLight = themeMode == LightMode;
        var primaryColor = GetBrushColor(resources, "PrimaryHueMidBrush", ParseColor("#F59E0B"));
        var secondaryColor = GetBrushColor(resources, "SecondaryHueMidBrush", ParseColor("#84CC16"));

        var headerBackgroundColor = ParseColor(isLight ? "#F3F4F6" : "#000000");
        var headerForegroundColor = ParseColor(isLight ? "#111827" : "#FFFFFF");

        // Los brushes definidos en XAML pueden quedar congelados (read-only),
        // por eso reemplazamos el recurso en vez de mutar la propiedad Color.
        SetBrush(resources, "AppPrimaryBrush", primaryColor);
        SetBrush(resources, "AppSecondaryBrush", secondaryColor);
        SetBrush(resources, "AppBorderBrush", primaryColor);
        SetBrush(resources, "AppIconBrush", primaryColor);
        SetBrush(resources, "HeaderBackground", headerBackgroundColor);
        SetBrush(resources, "HeaderForeground", headerForegroundColor);
        SetBrush(resources, "ActionTextOnAccentBrush", GetReadableForeground(primaryColor));
        SetBrush(resources, "ActionTextOnSecondaryBrush", GetReadableForeground(secondaryColor));

        // Brushes semanticos para estados y diffs.
        SetBrush(resources, "StatusDangerBrush", ParseColor(isLight ? "#C62828" : "#FF6B6B"));
        SetBrush(resources, "StatusSuccessBrush", ParseColor(isLight ? "#2E7D32" : "#5EDC7A"));
        SetBrush(resources, "StatusWarningBrush", ParseColor(isLight ? "#D97706" : "#F59E0B"));
        SetBrush(resources, "StatusWarningMutedBackgroundBrush", ParseColor(isLight ? "#FFFBEB" : "#2D240F"));
        SetBrush(resources, "StatusWarningSoftBorderBrush", ParseColor(isLight ? "#FDE68A" : "#A16207"));
        SetBrush(resources, "StatusInfoBrush", ParseColor(isLight ? "#0284C7" : "#38BDF8"));
        SetBrush(resources, "StatusPriorityMediumBrush", ParseColor(isLight ? "#CA8A04" : "#FACC15"));

        SetBrush(resources, "StatusDangerMutedBackgroundBrush", ParseColor(isLight ? "#33F44336" : "#331010"));
        SetBrush(resources, "StatusSuccessMutedBackgroundBrush", ParseColor(isLight ? "#3322C55E" : "#103310"));
        SetBrush(resources, "StatusInfoMutedBackgroundBrush", ParseColor(isLight ? "#330284C7" : "#0F2436"));

        SetBrush(resources, "StatusDiffInsertedBackgroundBrush", ParseColor(isLight ? "#3322C55E" : "#102510"));
        SetBrush(resources, "StatusDiffDeletedBackgroundBrush", ParseColor(isLight ? "#33F44336" : "#251010"));
        SetBrush(resources, "StatusDiffInsertedForegroundBrush", ParseColor(isLight ? "#2E7D32" : "#80C880"));
        SetBrush(resources, "StatusDiffDeletedForegroundBrush", ParseColor(isLight ? "#C62828" : "#C88080"));
        SetBrush(resources, "StatusSelectionOverlayBrush", BuildSelectionOverlay(primaryColor, isLight));
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        resources[key] = new SolidColorBrush(color);
    }

    private static Color GetBrushColor(ResourceDictionary resources, string key, Color fallback)
    {
        if (resources.Contains(key) && resources[key] is SolidColorBrush brush)
            return brush.Color;

        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush appBrush)
            return appBrush.Color;

        return fallback;
    }

    private static Color GetReadableForeground(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance >= 0.58 ? ParseColor("#111827") : ParseColor("#FFFFFF");
    }

    private static Color BuildSelectionOverlay(Color baseColor, bool isLight)
    {
        var alpha = (byte)(isLight ? 0x40 : 0x33);
        return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
    }

    private static Color ParseColor(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }
}
