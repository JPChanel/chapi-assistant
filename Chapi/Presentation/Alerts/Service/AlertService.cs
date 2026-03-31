using Chapi.Presentation.Alerts.Models;
using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Alerts.Service;

public sealed class AlertService : IAlertService
{
    public event EventHandler<EstNotificationMessage>? AlertRaised;

    public void Show(string message, string title = "Notificacion", AlertVariant variant = AlertVariant.Info, PackIconKind? icon = null, TimeSpan? duration = null)
    {
        AlertRaised?.Invoke(this, new EstNotificationMessage
        {
            Message = message,
            Title = title,
            Variant = variant,
            IconKind = icon,
            Duration = duration
        });
    }
}
