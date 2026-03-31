using Chapi.Presentation.Alerts.Models;
using Chapi.Presentation.Alerts.Service;
using Chapi.Presentation.Alerts.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chapi.Presentation.Alerts.ViewModels;

public partial class NotificationHostViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string currentTitle = string.Empty;

    [ObservableProperty]
    private string currentMessage = string.Empty;

    [ObservableProperty]
    private Brush currentBackgroundBrush = Brushes.White;

    [ObservableProperty]
    private Brush currentBorderBrush = Brushes.Transparent;

    [ObservableProperty]
    private Brush currentForegroundBrush = Brushes.Black;

    [ObservableProperty]
    private PackIconKind currentIconKind = PackIconKind.InformationOutline;

    public NotificationHostViewModel(IAlertService alertService)
    {
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            IsOpen = false;
        };

        alertService.AlertRaised += OnAlertRaised;
    }

    private void OnAlertRaised(object? sender, EstNotificationMessage alert)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentTitle = alert.Title;
            CurrentMessage = alert.Message;
            CurrentIconKind = alert.IconKind ?? AlertStyleHelper.GetDefaultIcon(alert.Variant);

            var palette = AlertStyleHelper.ResolvePalette(alert.Variant);
            CurrentBackgroundBrush = palette.Background;
            CurrentBorderBrush = palette.Border;
            CurrentForegroundBrush = palette.Foreground;

            IsOpen = true;
            _timer.Interval = alert.Duration ?? TimeSpan.FromSeconds(4);
            _timer.Stop();
            _timer.Start();
        });
    }
}
