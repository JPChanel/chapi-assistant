using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using app_desktop_base.Models.EstDataTable;
using app_desktop_base.Utilities;
using MaterialDesignThemes.Wpf;

namespace app_desktop_base.Controls;

public partial class EstDataCellPresenter : UserControl
{
    public static readonly DependencyProperty ColumnDefinitionProperty =
        DependencyProperty.Register(nameof(ColumnDefinition), typeof(EstDataColumnDefinition), typeof(EstDataCellPresenter), new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(object), typeof(EstDataCellPresenter), new PropertyMetadata(null, OnInputChanged));

    private ResolvedCellPresentation? _resolvedPresentation;

    public EstDataCellPresenter()
    {
        InitializeComponent();
    }

    public EstDataColumnDefinition? ColumnDefinition
    {
        get => (EstDataColumnDefinition?)GetValue(ColumnDefinitionProperty);
        set => SetValue(ColumnDefinitionProperty, value);
    }

    public object? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EstDataCellPresenter)d).ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        _resolvedPresentation = ResolvePresentation();
        if (_resolvedPresentation is null)
        {
            return;
        }

        var hasContextMenu = _resolvedPresentation.ContextMenu is not null;
        var isClickable = hasContextMenu || _resolvedPresentation.Command?.CanExecute(_resolvedPresentation.CommandParameter) == true;

        StaticBorder.Visibility = isClickable ? Visibility.Collapsed : Visibility.Visible;
        ClickButton.Visibility = isClickable ? Visibility.Visible : Visibility.Collapsed;
        ClickButton.ToolTip = _resolvedPresentation.ToolTip;
        ClickButton.ContextMenu = _resolvedPresentation.ContextMenu;
        StaticBorder.ToolTip = _resolvedPresentation.ToolTip;

        ApplySurface(StaticBorder, StaticIcon, StaticText, _resolvedPresentation, isLinkVisual: false);
        ApplySurface(ClickBorder, ClickIcon, ClickText, _resolvedPresentation, isLinkVisual: true);
    }

    private void ClickButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_resolvedPresentation?.ContextMenu is ContextMenu contextMenu && sender is FrameworkElement element)
        {
            contextMenu.PlacementTarget = element;
            contextMenu.IsOpen = true;
            return;
        }

        if (_resolvedPresentation?.Command?.CanExecute(_resolvedPresentation.CommandParameter) == true)
        {
            _resolvedPresentation.Command.Execute(_resolvedPresentation.CommandParameter);
        }
    }

    private ResolvedCellPresentation ResolvePresentation()
    {
        var definition = ColumnDefinition;
        if (definition is null)
        {
            return CreateFallbackPresentation();
        }

        var item = Item;
        var custom = definition.CellPresentationSelector?.Invoke(item);
        var rawValue = ResolveRawValue(definition, item);
        var variant = definition.CellVariantSelector?.Invoke(item) ?? definition.CellVariant;

        var isLink = (custom?.IsLink
            ?? definition.IsLinkSelector?.Invoke(item)
            ?? definition.IsLink)
            || variant == EstDataCellVariant.Link;

        var useContainer = (custom?.UseContainer
            ?? definition.UseContainerSelector?.Invoke(item)
            ?? definition.UseContainer)
            || variant is EstDataCellVariant.Filled or EstDataCellVariant.Outline;

        var foreground = custom?.Foreground
            ?? definition.ForegroundSelector?.Invoke(item)
            ?? definition.Foreground
            ?? ResolveVariantForeground(variant, isLink);

        var background = custom?.Background
            ?? definition.BackgroundSelector?.Invoke(item)
            ?? definition.Background
            ?? ResolveVariantBackground(variant, useContainer);

        var borderBrush = custom?.BorderBrush
            ?? definition.BorderBrushSelector?.Invoke(item)
            ?? definition.BorderBrush
            ?? ResolveVariantBorderBrush(variant, useContainer);

        var borderThickness = custom?.BorderThickness
            ?? definition.BorderThicknessSelector?.Invoke(item)
            ?? definition.BorderThickness
            ?? ResolveVariantBorderThickness(variant, useContainer);

        var padding = custom?.Padding
            ?? definition.PaddingSelector?.Invoke(item)
            ?? definition.Padding
            ?? ResolveVariantPadding(variant, useContainer);

        var cornerRadius = custom?.CornerRadius
            ?? definition.CornerRadiusSelector?.Invoke(item)
            ?? definition.CornerRadius
            ?? ResolveVariantCornerRadius(variant, useContainer);

        var fontWeight = custom?.FontWeight
            ?? definition.FontWeightSelector?.Invoke(item)
            ?? definition.FontWeight
            ?? FontWeights.Normal;

        var underline = custom?.Underline
            ?? definition.UnderlineSelector?.Invoke(item)
            ?? definition.Underline
            || isLink;

        var iconKind = custom?.IconKind
            ?? definition.IconSelector?.Invoke(item)
            ?? definition.IconKind;

        var text = custom?.Text
            ?? definition.TextSelector?.Invoke(item)
            ?? definition.Text
            ?? FormatCellValue(definition, rawValue);

        var command = custom?.Command
            ?? definition.ClickCommandSelector?.Invoke(item)
            ?? definition.ClickCommand;

        var commandParameter = custom?.CommandParameter
            ?? definition.ClickCommandParameterSelector?.Invoke(item)
            ?? definition.ClickCommandParameter
            ?? item;

        var toolTip = custom?.ToolTip
            ?? definition.ToolTipSelector?.Invoke(item)
            ?? definition.ToolTip;

        var contextMenu = custom?.ContextMenu
            ?? definition.ContextMenuSelector?.Invoke(item)
            ?? definition.ContextMenu;

        return new ResolvedCellPresentation(
            text,
            iconKind,
            foreground,
            background,
            borderBrush,
            borderThickness,
            padding,
            cornerRadius,
            fontWeight,
            isLink,
            underline,
            command,
            commandParameter,
            toolTip,
            contextMenu);
    }

    private static object? ResolveRawValue(EstDataColumnDefinition definition, object? item)
    {
        if (definition.ValueSelector is not null)
        {
            return definition.ValueSelector(item);
        }

        if (definition.Value is not null)
        {
            return definition.Value;
        }

        if (!string.IsNullOrWhiteSpace(definition.ResolvedAccessorKey) && item is not null)
        {
            return PropertyPathHelper.GetValue(item, definition.ResolvedAccessorKey);
        }

        return item;
    }

    private static string FormatCellValue(EstDataColumnDefinition definition, object? value)
    {
        if (value is null)
        {
            return "-";
        }

        if (!string.IsNullOrWhiteSpace(definition.StringFormat) && value is IFormattable formattable)
        {
            return formattable.ToString(definition.StringFormat, CultureInfo.CurrentCulture);
        }

        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "-";
    }

    private static Brush ResolveFallbackBrush(string resourceKey, Brush fallback)
    {
        return Application.Current.Resources[resourceKey] as Brush ?? fallback;
    }

    private static Brush ResolveVariantForeground(EstDataCellVariant variant, bool isLink)
    {
        if (isLink || variant == EstDataCellVariant.Link)
        {
            return ResolveFallbackBrush("PrimaryActionForegroundBrush", Brushes.DodgerBlue);
        }

        return variant switch
        {
            EstDataCellVariant.Filled => ResolveFallbackBrush("TextSecondaryBrush", Brushes.DimGray),
            EstDataCellVariant.Outline => ResolveFallbackBrush("TextSecondaryBrush", Brushes.DimGray),
            _ => ResolveFallbackBrush("TextPrimaryBrush", Brushes.Black)
        };
    }

    private static Brush ResolveVariantBackground(EstDataCellVariant variant, bool useContainer)
    {
        if (!useContainer)
        {
            return Brushes.Transparent;
        }

        return variant switch
        {
            EstDataCellVariant.Filled => ResolveFallbackBrush("CardMutedBackgroundBrush", Brushes.Gainsboro),
            EstDataCellVariant.Outline => Brushes.Transparent,
            _ => ResolveFallbackBrush("CardMutedBackgroundBrush", Brushes.Gainsboro)
        };
    }

    private static Brush ResolveVariantBorderBrush(EstDataCellVariant variant, bool useContainer)
    {
        if (!useContainer)
        {
            return Brushes.Transparent;
        }

        return ResolveFallbackBrush("CardBorderBrush", Brushes.Transparent);
    }

    private static Thickness ResolveVariantBorderThickness(EstDataCellVariant variant, bool useContainer)
    {
        if (!useContainer)
        {
            return new Thickness(0);
        }

        return variant == EstDataCellVariant.Filled ? new Thickness(0) : new Thickness(1);
    }

    private static Thickness ResolveVariantPadding(EstDataCellVariant variant, bool useContainer)
    {
        return useContainer ? new Thickness(10, 3, 10, 3) : new Thickness(0);
    }

    private static CornerRadius ResolveVariantCornerRadius(EstDataCellVariant variant, bool useContainer)
    {
        return new(useContainer ? 10 : 0);
    }

    private void ApplySurface(Border border, PackIcon icon, TextBlock textBlock, ResolvedCellPresentation presentation, bool isLinkVisual)
    {
        border.Background = presentation.Background;
        border.BorderBrush = presentation.BorderBrush;
        border.BorderThickness = presentation.BorderThickness;
        border.Padding = presentation.Padding;
        border.CornerRadius = presentation.CornerRadius;

        icon.Visibility = presentation.IconKind is null ? Visibility.Collapsed : Visibility.Visible;
        if (presentation.IconKind is not null)
        {
            icon.Kind = presentation.IconKind.Value;
            icon.Foreground = presentation.Foreground;
        }

        textBlock.Text = presentation.Text;
        textBlock.Foreground = presentation.Foreground;
        textBlock.FontWeight = presentation.FontWeight;
        textBlock.TextDecorations = presentation.IsLink && presentation.Underline && isLinkVisual
            ? TextDecorations.Underline
            : null;
    }

    private static ResolvedCellPresentation CreateFallbackPresentation()
    {
        return new ResolvedCellPresentation(
            string.Empty,
            null,
            Brushes.Black,
            Brushes.Transparent,
            Brushes.Transparent,
            new Thickness(0),
            new Thickness(0),
            new CornerRadius(0),
            FontWeights.Normal,
            false,
            false,
            null,
            null,
            null,
            null);
    }

    private sealed record ResolvedCellPresentation(
        string Text,
        PackIconKind? IconKind,
        Brush Foreground,
        Brush Background,
        Brush BorderBrush,
        Thickness BorderThickness,
        Thickness Padding,
        CornerRadius CornerRadius,
        FontWeight FontWeight,
        bool IsLink,
        bool Underline,
        ICommand? Command,
        object? CommandParameter,
        string? ToolTip,
        ContextMenu? ContextMenu);
}
