using Chapi.Application.Services.Assistant;
using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities.Assistant;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;

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

    public AssistantViewModel(ConversationManager conversationManager, IAssistantCapabilityRegistry capabilityRegistry)
    {
        _conversationManager = conversationManager;
        _capabilityRegistry = capabilityRegistry;

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
                       "Estoy aquí para ayudarte con:\n" +
                       "🔎 Explicar la arquitectura de tu proyecto\n" +
                       "📝 Analizar commits y cambios recientes\n" +
                       "🌿 Resolver dudas sobre Git y control de versiones\n" +
                       "🤖 Ejecutar operaciones del asistente Chapi\n\n" +
                       "Cuéntame, ¿qué necesitas hacer hoy? 🚀",
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

            if (MainWindow.Instance != null)
            {
                await MainWindow.Instance.UpdateProjectStatusesAsync();
            }
        }
        catch (Exception ex)
        {
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
                var shouldPush = intent.Parameters.GetValueOrDefault("push", "false").ToLower() == "true";

                var context = _conversationManager.GetCurrentProjectContext();
                var files = new List<string>();
                if (context?.Git != null)
                {
                    files.AddRange(context.Git.ModifiedFilePaths); // Usar rutas limpias
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

                if (cResult.IsSuccess && shouldPush)
                {
                    lock (_messagesLock) Messages.Add(new ChatMessage { Text = "🚀 Iniciando push automático...", Author = MessageAuthor.Assistant });
                    
                    var pushUC = App.ServiceProvider.GetService(typeof(PushChangesUseCase)) as PushChangesUseCase;
                    if (pushUC != null)
                    {
                        var branch = context?.Git?.CurrentBranch ?? "main";
                        var pushRes = await pushUC.ExecuteAsync(_currentProjectPath, branch);
                        lock (_messagesLock)
                        {
                            Messages.Add(new ChatMessage { Text = pushRes.IsSuccess ? $"✅ Push completado." : $"❌ Error en Push: {pushRes.Error}", Author = MessageAuthor.Assistant });
                        }
                    }
                }
                break;

            case PushChangesUseCase pushUC:
                var pushBranch = _conversationManager.GetCurrentProjectContext()?.Git?.CurrentBranch ?? "main";
                var pResult = await pushUC.ExecuteAsync(_currentProjectPath, pushBranch);
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
                }
                break;

            case FetchChangesUseCase fetchUC:
                var fResult = await fetchUC.ExecuteAsync(_currentProjectPath, isSilent: false);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = fResult.IsSuccess ? $"✅ {capability.Name} completado." : $"❌ {fResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case Chapi.Application.UseCases.Projects.CloneProjectUseCase cloneUC:
                // 1. Validar URL del repositorio (Obligatorio)
                if (!intent.Parameters.TryGetValue("url", out var repoUrl) || string.IsNullOrWhiteSpace(repoUrl))
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = "🔗 Necesito la URL del repositorio para clonar. ¿Cuál es?", Author = MessageAuthor.Assistant });
                    }
                    break;
                }

                // 2. Validar ruta destino (Obligatorio)
                if (!intent.Parameters.TryGetValue("path", out var clonePath) || string.IsNullOrWhiteSpace(clonePath))
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = "📂 ¿En qué carpeta quieres que clone este repositorio?", Author = MessageAuthor.Assistant });
                    }
                    break;
                }

                lock (_messagesLock) Messages.Add(new ChatMessage { Text = $"⏳ Clonando desde '{repoUrl}' en '{clonePath}'...", Author = MessageAuthor.Assistant });

                var cloneResult = await cloneUC.ExecuteAsync(repoUrl, clonePath);

                if (cloneResult.IsSuccess)
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = $"✅ Clonado exitoso en {cloneResult.Data}. Cambiando contexto...", Author = MessageAuthor.Assistant });
                    }
                    // Cambiar al nuevo proyecto fuera del lock
                    await UpdateProjectContextAsync(cloneResult.Data, true);
                }
                else
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = $"❌ Error al clonar: {cloneResult.Error}", Author = MessageAuthor.Assistant });
                    }
                }
                break;

            case Chapi.Application.UseCases.Projects.CreateProjectUseCase createUC:
                // 1. Validar nombre del proyecto (Obligatorio)
                if (!intent.Parameters.TryGetValue("name", out var pName) || string.IsNullOrWhiteSpace(pName))
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = "📝 Para crear el proyecto necesito un nombre. ¿Cómo quieres llamarlo?", Author = MessageAuthor.Assistant });
                    }
                    break;
                }

                // 2. Validar ruta destino (Obligatorio)
                if (!intent.Parameters.TryGetValue("path", out var pPath) || string.IsNullOrWhiteSpace(pPath))
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = "📂 ¿En qué carpeta quieres que cree el proyecto?", Author = MessageAuthor.Assistant });
                    }
                    break;
                }

                // 3. Template (Por defecto Clean Architecture, pero configurable)
                var pTemplate = intent.Parameters.GetValueOrDefault("template", "https://github.com/Start-Z/CleanArchitecture-Template.git");

                lock (_messagesLock) Messages.Add(new ChatMessage { Text = $"⏳ Creando proyecto '{pName}' en '{pPath}' usando plantilla Clean Architecture...", Author = MessageAuthor.Assistant });

                var createReq = new Chapi.Application.UseCases.Projects.CreateProjectRequest(pName, pPath, pTemplate);
                var createResult = await createUC.ExecuteAsync(createReq);

                if (createResult.IsSuccess)
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = $"✅ Proyecto creado en {createResult.Data}. Cambiando contexto...", Author = MessageAuthor.Assistant });
                    }
                    await UpdateProjectContextAsync(createResult.Data, true);
                }
                else
                {
                    lock (_messagesLock)
                    {
                        Messages.Add(new ChatMessage { Text = $"❌ Error al crear proyecto: {createResult.Error}", Author = MessageAuthor.Assistant });
                    }
                }
                break;

            case Chapi.Application.UseCases.Projects.LoadProjectsUseCase listUC:
                var listResult = await listUC.ExecuteAsync();
                lock (_messagesLock)
                {
                    if (listResult.IsSuccess)
                    {
                        var fileList = string.Join("\n", listResult.Data.Select(p => $"• {p.Name} ({p.FullPath})"));
                        Messages.Add(new ChatMessage { Text = $"📂 **Mis Proyectos**:\n{fileList}", Author = MessageAuthor.Assistant });
                    }
                    else
                    {
                        Messages.Add(new ChatMessage { Text = $"❌ Error al listar: {listResult.Error}", Author = MessageAuthor.Assistant });
                    }
                }
                break;

            case Chapi.Application.UseCases.Projects.AddProjectUseCase addProjectUC:
                var addPath = intent.Parameters.GetValueOrDefault("path", "");
                if (string.IsNullOrEmpty(addPath))
                {
                    lock (_messagesLock) Messages.Add(new ChatMessage { Text = "❌ Por favor indica la ruta de la carpeta.", Author = MessageAuthor.Assistant });
                    break;
                }
                var addResult = await addProjectUC.ExecuteAsync(addPath);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = addResult.IsSuccess ? $"✅ Proyecto agregado a la lista." : $"❌ {addResult.Error}", Author = MessageAuthor.Assistant });
                }
                break;

            case Chapi.Application.UseCases.Projects.RemoveProjectUseCase removeProjectUC:
                // Usamos la ruta actual si no se especifica otra
                var remPath = intent.Parameters.GetValueOrDefault("path", _currentProjectPath);
                var remResult = await removeProjectUC.ExecuteAsync(remPath);
                lock (_messagesLock)
                {
                    Messages.Add(new ChatMessage { Text = remResult.IsSuccess ? $"✅ Proyecto removido de la lista." : $"❌ {remResult.Error}", Author = MessageAuthor.Assistant });
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
