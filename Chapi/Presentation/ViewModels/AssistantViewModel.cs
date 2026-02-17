using System.Collections.ObjectModel;
using System.Windows.Input;
using Chapi.Application.Services.Assistant;
using Chapi.Domain.Entities.Assistant;
using Chapi.Infrastructure.Services;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Enums;
using Chapi.Application.UseCases.Git;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Data;

namespace Chapi.Presentation.ViewModels;

public class AssistantViewModel : ViewModelBase
{
    private readonly ConversationManager _conversationManager;
    private readonly IAssistantCapabilityRegistry _capabilityRegistry;
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

    private readonly object _messagesLock = new object();

    public AssistantViewModel()
    {
        _conversationManager = new ConversationManager();
        _capabilityRegistry = App.ServiceProvider.GetRequiredService<IAssistantCapabilityRegistry>();
        
        // Habilitar sincronización para que la colección pueda ser modificada desde hilos secundarios
        BindingOperations.EnableCollectionSynchronization(Messages, _messagesLock);

        SendMessageCommand = new AsyncRelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
        ClearConversationCommand = new RelayCommand(_ => ClearConversation());
        ExecuteActionCommand = new AsyncRelayCommand(async param => await ExecuteActionAsync(param as ChatMessage));

        // Mensaje de bienvenida
        AddWelcomeMessage();
    }

    private void AddWelcomeMessage()
    {
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Text = "👋 ¡Hola! Soy tu asistente de desarrollo.\n\n" +
                       "Puedo ayudarte con:\n" +
                       "• Explica la arquitectura de tu proyecto\n" +
                       "• Analizar commits y cambios recientes\n" +
                       "• Resolver dudas sobre Git\n" +
                       "• Sugerir mejoras de código\n" +
                       "• Generar código siguiendo tus patrones\n\n" +
                       "¿En qué puedo ayudarte hoy? 🚀",
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            });
        }
    }

    public async Task UpdateProjectContextAsync(string projectPath, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return;

        bool isNewProject = projectPath != _currentProjectPath;
        if (!isNewProject && !forceRefresh)
            return;

        _currentProjectPath = projectPath;
        await _conversationManager.UpdateProjectContextAsync(projectPath);

        if (isNewProject)
        {
            // Notificar cambio de proyecto solo si es nuevo
            var projectContext = _conversationManager.GetCurrentProjectContext();
            if (projectContext != null)
            {
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage
                    {
                        Text = $"📁 Proyecto actualizado: **{projectContext.Name}**\n" +
                               $"🔧 Tecnología: {projectContext.Technology}",
                        Author = MessageAuthor.Assistant,
                        Timestamp = DateTime.Now
                    });
                }
                ScrollToBottom?.Invoke();
            }
        }

        // Siempre refrescar las otras pestañas para mantener la coherencia
        await RefreshUIAsync();
    }

    private async Task RefreshUIAsync()
    {
        try
        {
            var historyVM = App.ServiceProvider.GetService<HistoryViewModel>();
            var changesVM = App.ServiceProvider.GetService<ChangesViewModel>();

            if (historyVM != null) await historyVM.RefreshCommand.ExecuteAsync(null);
            if (changesVM != null) await changesVM.ForceRefreshAsync();
            
            // Refrescar indicadores globales en el combo de proyectos y el botón Git (MainWindow)
            if (MainWindow.Instance != null)
            {
                await MainWindow.Instance.UpdateProjectStatusesAsync();
            }
        }
        catch (Exception ex)
        {
            // Error silencioso en refresco de UI para no interrumpir el flujo del asistente
            System.Diagnostics.Debug.WriteLine($"Error al refrescar UI: {ex.Message}");
        }
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentMessage))
            return;

        var userMessage = CurrentMessage.Trim();
        CurrentMessage = string.Empty;

        // Agregar mensaje del usuario a la UI
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Text = userMessage,
                Author = MessageAuthor.User,
                Timestamp = DateTime.Now
            });
        }

        ScrollToBottom?.Invoke();

        IsProcessing = true;

        try
        {
            // Procesar mensaje y obtener respuesta
            var result = await _conversationManager.ProcessUserMessageAsync(userMessage);

            lock (_messagesLock)
            {
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
        }
        catch (Exception ex)
        {
            lock (_messagesLock)
            {
                Messages.Add(new ChatMessage
                {
                    Text = $"❌ Ocurrió un error inesperado. Por favor, intenta de nuevo.",
                    Author = MessageAuthor.Assistant,
                    Timestamp = DateTime.Now
                });
            }
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

        var intent = message.Action;
        if (string.IsNullOrEmpty(intent.CapabilityId))
        {
            // Fallback al switch manual si no hay CapabilityId (por compatibilidad temporal o intenciones no mapeadas a UseCases)
            await ExecuteManualIntentAsync(message);
            return;
        }

        var capability = _capabilityRegistry.GetAllCapabilities().FirstOrDefault(c => c.Id == intent.CapabilityId);
        if (capability == null) return;

        IsProcessing = true;
        try
        {
            // Obtener el UseCase del ServiceProvider
            var useCase = App.ServiceProvider.GetService(capability.TargetUseCaseType);
            if (useCase == null)
            {
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = $"❌ Error: No se pudo encontrar el motor para la acción '{capability.Name}'.", Author = MessageAuthor.Assistant });
                }
                return;
            }

            // Ejecutar según el tipo de UseCase (Mapeo dinámico a los ExecuteAsync existentes)
            await DispatchUseCaseAsync(useCase, intent, capability);
            
            message.Action = null; // Quitar el botón de acción después de ejecutar
            await UpdateProjectContextAsync(_currentProjectPath, forceRefresh: true);
        }
        catch (Exception ex)
        {
            lock (_messagesLock)
            {
                Messages.Add(new ChatMessage { Text = $"❌ Error al ejecutar {capability.Name}: {ex.Message}", Author = MessageAuthor.Assistant });
            }
        }
        finally
        {
            IsProcessing = false;
            ScrollToBottom?.Invoke();
        }
    }

    private async Task DispatchUseCaseAsync(object useCase, UserIntent intent, Domain.Models.Assistant.AssistantCapability capability)
    {
        // Nota: Este despachador asume que los UseCases siguen la convención de tener un método ExecuteAsync
        // Se puede mejorar usando una interfaz común si todos los UseCases la implementaran (IUseCase)
        
        switch (useCase)
        {
            case CommitChangesUseCase commitUC:
                var summary = intent.Parameters.GetValueOrDefault("message", "Commit desde el Asistente");
                var description = intent.Parameters.GetValueOrDefault("description", "");
                var fullMsg = string.IsNullOrEmpty(description) ? summary : $"{summary}\n\n{description}";
                
                var context = _conversationManager.GetCurrentProjectContext();
                var files = new List<string>();
                if (context?.Git != null) {
                    files.AddRange(context.Git.ModifiedFiles);
                    files.AddRange(context.Git.UntrackedFiles);
                }
                var cResult = await commitUC.ExecuteAsync(new CommitRequest 
                { 
                    ProjectPath = _currentProjectPath, 
                    Message = fullMsg, 
                    Files = files 
                });
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = cResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {cResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case PushChangesUseCase pushUC:
                var branch = _conversationManager.GetCurrentProjectContext()?.Git?.CurrentBranch ?? "main";
                var pResult = await pushUC.ExecuteAsync(_currentProjectPath, branch);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = pResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {pResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case PullChangesUseCase pullUC:
                var pBranch = _conversationManager.GetCurrentProjectContext()?.Git?.CurrentBranch ?? "main";
                var plResult = await pullUC.ExecuteAsync(_currentProjectPath, pBranch, true);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = plResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {plResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case StashChangesUseCase stashUC:
                var sMsg = intent.Parameters.GetValueOrDefault("message", "Stash desde el Asistente");
                var sResult = await stashUC.ExecuteAsync(_currentProjectPath, sMsg);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = sResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {sResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case StashPopUseCase popUC:
                var stResult = await popUC.ExecuteAsync(_currentProjectPath);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = stResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {stResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case ResetCommitUseCase undoUC:
                var uResult = await undoUC.ExecuteAsync(_currentProjectPath, ResetMode.Soft);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = uResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {uResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case SwitchBranchUseCase switchUC:
                var swBranch = intent.Parameters.GetValueOrDefault("branch", "");
                if (string.IsNullOrEmpty(swBranch)) swBranch = intent.Parameters.GetValueOrDefault("branchName", "");
                var swResult = await switchUC.ExecuteAsync(_currentProjectPath, swBranch);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = swResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {swResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case CreateBranchUseCase createBranchUC:
                var newBranch = intent.Parameters.GetValueOrDefault("name", "");
                if (string.IsNullOrEmpty(newBranch)) newBranch = intent.Parameters.GetValueOrDefault("branchName", "");
                var cbResult = await createBranchUC.ExecuteAsync(_currentProjectPath, newBranch);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = cbResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {cbResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case CreateTagUseCase createTagUC:
                var tagName = intent.Parameters.GetValueOrDefault("name", "");
                if (string.IsNullOrEmpty(tagName)) tagName = intent.Parameters.GetValueOrDefault("tagName", "");
                var tMsg = intent.Parameters.GetValueOrDefault("message", $"Tag {tagName} desde Asistente");
                var ctResult = await createTagUC.ExecuteAsync(_currentProjectPath, tagName, tMsg);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = ctResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {ctResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case DiscardChangesUseCase discardUC:
                var dResult = await discardUC.ExecuteAsync(_currentProjectPath);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = dResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {dResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            default:
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = $"⚠️ La capacidad '{capability.Name}' está registrada pero el despachador no sabe cómo llamarla.", Author = MessageAuthor.Assistant });
                }
                break;
        }
    }

    private async Task ExecuteManualIntentAsync(ChatMessage message)
    {
        // Mantener lógica anterior para compatibilidad si es necesario
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage { Text = "⚠️ Esta acción requiere ser mapeada al nuevo sistema de capacidades.", Author = MessageAuthor.Assistant });
        }
    }

    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(CurrentMessage) && !IsProcessing;
    }

    private void ClearConversation()
    {
        lock (_messagesLock)
        {
            Messages.Clear();
            AddWelcomeMessage();
        }
    }
}
