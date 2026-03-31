using MaterialDesignThemes.Wpf;

namespace Chapi.Presentation.Shared.Notifications.Models;

public sealed class EstNotificationMessage
{
    public string Title { get; init; } = "Notificacion";

    public string Message { get; init; } = string.Empty;

    public AlertVariant Variant { get; init; } = AlertVariant.Info;

    public PackIconKind? IconKind { get; init; }

    public TimeSpan? Duration { get; init; }
}
