using Chapi.Domain.Interfaces;
using Chapi.Presentation.Alerts.Models;
using Chapi.Presentation.Alerts.Service;

namespace Chapi.Infrastructure.Services;

/// <summary>
/// Implementacion del servicio de notificaciones usando MessageHelper existente.
/// </summary>
public class MessageNotificationService : INotificationService
{
    private readonly IAlertService _alertService;

    public MessageNotificationService(IAlertService alertService)
    {
        _alertService = alertService;
    }

    public void ShowSuccess(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message, showAlert: false);
        _alertService.Show(message, title: "Correcto", variant: AlertVariant.Success);
    }

    public void ShowError(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message, showAlert: false);
        _alertService.Show(message, title: "Error", variant: AlertVariant.Error, duration: TimeSpan.FromSeconds(6));
    }

    public void ShowInfo(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message, showAlert: false);
        _alertService.Show(message, title: "Información", variant: AlertVariant.Info);
    }

    public void ShowWarning(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message, showAlert: false);
        _alertService.Show(message, title: "Aviso", variant: AlertVariant.Warning, duration: TimeSpan.FromSeconds(5));
    }
}
