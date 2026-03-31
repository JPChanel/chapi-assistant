using Chapi.Presentation.Alerts.Models;
using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Alerts.Service;

public interface IAlertService
{
    event EventHandler<EstNotificationMessage> AlertRaised;

    void Show(string message, string title = "Notificacion", AlertVariant variant = AlertVariant.Info, PackIconKind? icon = null, TimeSpan? duration = null);
}
