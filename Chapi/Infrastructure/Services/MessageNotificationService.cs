using Chapi.Domain.Interfaces;


namespace Chapi.Infrastructure.Services;

/// <summary>
/// Implementacion del servicio de notificaciones usando MessageHelper existente.
/// </summary>
public class MessageNotificationService : INotificationService
{
    public void ShowSuccess(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message);
    }

    public void ShowError(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message);
    }

    public void ShowInfo(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message);
    }

    public void ShowWarning(string message)
    {
        MessageHelper.Instance.AddAssistantMessage(message);
    }
}


