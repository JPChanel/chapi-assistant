using System.Collections.ObjectModel;
using System.Windows.Input;
using Chapi.Application.Services.Assistant;
using Chapi.Domain.Entities.Assistant;
using Chapi.Infrastructure.Services;

namespace Chapi.Presentation.ViewModels;

public class AssistantViewModel : ViewModelBase
{
    private readonly ConversationManager _conversationManager;
    private string _currentMessage = string.Empty;
    private bool _isProcessing;
    private string _currentProjectPath = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string CurrentMessage
    {
        get => _currentMessage;
        set => SetProperty(ref _currentMessage, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public ICommand SendMessageCommand { get; }
    public ICommand ClearConversationCommand { get; }

    public event Action? ScrollToBottom;

    public AssistantViewModel()
    {
        _conversationManager = new ConversationManager();
        
        SendMessageCommand = new AsyncRelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
        ClearConversationCommand = new RelayCommand(_ => ClearConversation());

        // Mensaje de bienvenida
        AddWelcomeMessage();
    }

    private void AddWelcomeMessage()
    {
        Messages.Add(new ChatMessage
        {
            Text = "👋 ¡Hola! Soy tu asistente de desarrollo.\n\n" +
                   "Puedo ayudarte con:\n" +
                   "• Explicar la arquitectura de tu proyecto\n" +
                   "• Analizar commits y cambios recientes\n" +
                   "• Resolver dudas sobre Git\n" +
                   "• Sugerir mejoras de código\n" +
                   "• Generar código siguiendo tus patrones\n\n" +
                   "¿En qué puedo ayudarte hoy? 🚀",
            Author = MessageAuthor.Assistant,
            Timestamp = DateTime.Now
        });
    }

    public async Task UpdateProjectContextAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || projectPath == _currentProjectPath)
            return;

        _currentProjectPath = projectPath;
        await _conversationManager.UpdateProjectContextAsync(projectPath);

        // Notificar cambio de proyecto
        var projectContext = _conversationManager.GetCurrentProjectContext();
        if (projectContext != null)
        {
            Messages.Add(new ChatMessage
            {
                Text = $"📁 Proyecto actualizado: **{projectContext.Name}**\n" +
                       $"🔧 Tecnología: {projectContext.Technology}",
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            });

            ScrollToBottom?.Invoke();
        }
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentMessage))
            return;

        var userMessage = CurrentMessage.Trim();
        CurrentMessage = string.Empty;

        // Agregar mensaje del usuario a la UI
        Messages.Add(new ChatMessage
        {
            Text = userMessage,
            Author = MessageAuthor.User,
            Timestamp = DateTime.Now
        });

        ScrollToBottom?.Invoke();

        IsProcessing = true;

        try
        {
            // Procesar mensaje y obtener respuesta
            var result = await _conversationManager.ProcessUserMessageAsync(userMessage);

            if (result.IsSuccess && result.Data != null)
            {
                Messages.Add(result.Data);
            }
            else
            {
                Messages.Add(new ChatMessage
                {
                    Text = $"❌ Error: {result.Error}",
                    Author = MessageAuthor.Assistant,
                    Timestamp = DateTime.Now
                });
            }
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Error al procesar mensaje: {ex.Message}");
            Messages.Add(new ChatMessage
            {
                Text = $"❌ Ocurrió un error inesperado. Por favor, intenta de nuevo.",
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsProcessing = false;
            ScrollToBottom?.Invoke();
        }
    }

    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(CurrentMessage) && !IsProcessing;
    }

    private void ClearConversation()
    {
        Messages.Clear();
        _conversationManager.ClearConversation();
        AddWelcomeMessage();
    }
}
