namespace Chapi.Domain.Entities.Assistant;

/// <summary>
/// Mensaje individual en la conversación del asistente
/// </summary>
public class ChatMessage
{
    public string Text { get; set; } = string.Empty;
    public MessageAuthor Author { get; set; } = MessageAuthor.Assistant;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedTime => Timestamp.ToString("HH:mm");
}

/// <summary>
/// Autor del mensaje
/// </summary>
public enum MessageAuthor
{
    User,
    Assistant
}
