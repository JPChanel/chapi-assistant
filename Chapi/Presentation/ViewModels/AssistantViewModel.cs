using System.Collections.ObjectModel;
using Chapi.Domain.Entities;
using System.Windows.Data;

namespace Chapi.Presentation.ViewModels;

public class AssistantViewModel : ViewModelBase
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public AssistantViewModel()
    {
        // El ViewModel del asistente por ahora es simple para solucionar compilación
    }
}

public class ChatMessage
{
    public string Text { get; set; } = string.Empty;
    public string Author { get; set; } = "Assistant"; // "User" o "Assistant"
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm");
}
