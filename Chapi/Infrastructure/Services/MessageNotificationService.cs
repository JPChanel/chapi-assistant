using Chapi.Domain.Interfaces;
using Chapi.Helper;

namespace Chapi.Infrastructure.Services;

/// <summary>
/// Implementación del servicio de notificaciones usando MessageHelper existente.
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
