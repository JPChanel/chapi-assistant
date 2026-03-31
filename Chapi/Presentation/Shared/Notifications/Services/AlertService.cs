using Chapi.Presentation.Shared.Notifications.Models;
using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Shared.Notifications.Services;

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
