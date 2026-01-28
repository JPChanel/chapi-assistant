namespace Chapi.Domain.Interfaces;

/// <summary>
/// Servicio para mostrar notificaciones al usuario.
/// </summary>
public interface INotificationService
{
    void ShowSuccess(string message);
    void ShowError(string message);
    void ShowInfo(string message);
    void ShowWarning(string message);
}
