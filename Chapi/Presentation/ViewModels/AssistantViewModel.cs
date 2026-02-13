using System.Collections.ObjectModel;
using System.Windows.Input;
using Chapi.Application.Services.Assistant;
using Chapi.Domain.Entities.Assistant;
using Chapi.Infrastructure.Services;
using Chapi.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

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
    public ICommand ExecuteActionCommand { get; }

    public event Action? ScrollToBottom;

    public AssistantViewModel()
    {
        _conversationManager = new ConversationManager();
        
        SendMessageCommand = new AsyncRelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
        ClearConversationCommand = new RelayCommand(_ => ClearConversation());
        ExecuteActionCommand = new AsyncRelayCommand(async param => await ExecuteActionAsync(param as ChatMessage));

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

    private async Task ExecuteActionAsync(ChatMessage? message)
    {
        if (message?.Action == null || IsProcessing) return;

        IsProcessing = true;
        try
        {
            var context = _conversationManager.GetCurrentProjectContext();
            var gitRepository = App.ServiceProvider.GetRequiredService<IGitRepository>();
            var action = message.Action;

            switch (action.Type)
            {
                case IntentType.Commit:
                    if (action.Parameters.TryGetValue("message", out var commitMsg))
                    {
                        var filesToCommit = new List<string>();
                        if (context?.Git != null)
                        {
                            filesToCommit.AddRange(context.Git.ModifiedFiles);
                            filesToCommit.AddRange(context.Git.UntrackedFiles);
                        }

                        if (!filesToCommit.Any())
                        {
                            Messages.Add(new ChatMessage { Text = "⚠️ No hay archivos modificados para commitear.", Author = MessageAuthor.Assistant });
                            break;
                        }

                        var result = await gitRepository.CommitAsync(_currentProjectPath, commitMsg, filesToCommit);
                        if (result.IsSuccess)
                        {
                            Messages.Add(new ChatMessage { Text = "✅ Commit realizado con éxito.", Author = MessageAuthor.Assistant });
                            message.Action = null;
                            // Actualizar contexto después del commit
                            await UpdateProjectContextAsync(_currentProjectPath);
                        }
                        else
                        {
                            Messages.Add(new ChatMessage { Text = $"❌ Error al realizar commit: {result.Error}", Author = MessageAuthor.Assistant });
                        }
                    }
                    else
                    {
                        Messages.Add(new ChatMessage { Text = "❌ No se pudo encontrar un mensaje de commit válido.", Author = MessageAuthor.Assistant });
                    }
                    break;

                case IntentType.Push:
                    var pushBranch = context?.Git?.CurrentBranch ?? await gitRepository.GetCurrentBranchAsync(_currentProjectPath);
                    var pushResult = await gitRepository.PushAsync(_currentProjectPath, pushBranch);
                    if (pushResult.IsSuccess)
                    {
                        Messages.Add(new ChatMessage { Text = "✅ Cambios subidos (push) con éxito.", Author = MessageAuthor.Assistant });
                        message.Action = null;
                    }
                    else
                        Messages.Add(new ChatMessage { Text = $"❌ Error en push: {pushResult.Error}", Author = MessageAuthor.Assistant });
                    break;

                case IntentType.Pull:
                    var pullBranch = context?.Git?.CurrentBranch ?? await gitRepository.GetCurrentBranchAsync(_currentProjectPath);
                    var pullResult = await gitRepository.PullAsync(_currentProjectPath, pullBranch);
                    if (pullResult.IsSuccess)
                    {
                        Messages.Add(new ChatMessage { Text = "✅ Cambios descargados (pull) con éxito.", Author = MessageAuthor.Assistant });
                        message.Action = null;
                        await UpdateProjectContextAsync(_currentProjectPath);
                    }
                    else
                        Messages.Add(new ChatMessage { Text = $"❌ Error en pull: {pullResult.Error}", Author = MessageAuthor.Assistant });
                    break;

                default:
                    Messages.Add(new ChatMessage { Text = "⚠️ Lo siento, esta acción aún no está soportada.", Author = MessageAuthor.Assistant });
                    break;
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage { Text = $"❌ Ocurrió un error inesperado: {ex.Message}", Author = MessageAuthor.Assistant });
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
