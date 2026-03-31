using Chapi.Presentation.Shared.Notifications.Models;
using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Shared.Notifications.Services;

public interface IAlertService
{
    event EventHandler<EstNotificationMessage> AlertRaised;

    void Show(string message, string title = "Notificacion", AlertVariant variant = AlertVariant.Info, PackIconKind? icon = null, TimeSpan? duration = null);
}
