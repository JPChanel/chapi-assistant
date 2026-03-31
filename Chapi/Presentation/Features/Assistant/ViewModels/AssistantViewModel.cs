using Chapi.Application.Services.Assistant;
using Chapi.Application.UseCases.Git;
using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Entities.Assistant;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Notifications.Models;
using Chapi.Presentation.Shared.Notifications.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using Chapi.Presentation.Shared.Mvvm;
using Chapi.Presentation.Features.History.ViewModels;
using Chapi.Presentation.Features.Changes.ViewModels;

namespace Chapi.Presentation.Features.Assistant.ViewModels;

public class AssistantViewModel : ViewModelBase
{
    private readonly ConversationManager _conversationManager;
    private readonly IAssistantCapabilityRegistry _capabilityRegistry;
    private readonly IAlertService _alertService;
    private string _currentMessage = string.Empty;
    private bool _isProcessing;
    private string _currentProjectPath = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string CurrentMessage
    {
        get => _currentMessage;
        set
        {
            if (SetProperty(ref _currentMessage, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand SendMessageCommand { get; }
    public IRelayCommand ClearConversationCommand { get; }
    public IAsyncRelayCommand<ChatMessage?> ExecuteActionCommand { get; }

    public event Action? ScrollToBottom;

    private readonly object _messagesLock = new object();

    public AssistantViewModel(
        ConversationManager conversationManager,
        IAssistantCapabilityRegistry capabilityRegistry,
        IAlertService alertService)
    {
        _conversationManager = conversationManager;
        _capabilityRegistry = capabilityRegistry;
        _alertService = alertService;

        // Habilitar sincronizacion para que la coleccion pueda ser modificada desde hilos secundarios
        BindingOperations.EnableCollectionSynchronization(Messages, _messagesLock);

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
        ClearConversationCommand = new RelayCommand(ClearConversation);
        ExecuteActionCommand = new AsyncRelayCommand<ChatMessage?>(ExecuteActionAsync);

        AddWelcomeMessage();

        // Escuchar mensajes globales (Notificaciones de infraestructura)
        MessageHelper.Instance.MessageAdded += helperMsg =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_messagesLock)
                {
                    if (Messages.Any(m => m.Text == helperMsg.Text && m.Timestamp.ToString("HH:mm") == helperMsg.Timestamp))
                    {
                        return;
                    }

                    Messages.Add(new ChatMessage
                    {
                        Text = helperMsg.Text,
                        Author = helperMsg.Author == "User" ? MessageAuthor.User : MessageAuthor.Assistant,
                        Timestamp = DateTime.Now
                    });
                }

                ScrollToBottom?.Invoke();
            });
        };
    }

    private void AddWelcomeMessage()
    {
        AddAssistantMessage(
            "Hola. Soy tu asistente de desarrollo.\n\n" +
            "Estoy aqui para ayudarte con:\n" +
            "- Explicar la arquitectura de tu proyecto\n" +
            "- Analizar commits y cambios recientes\n" +
            "- Resolver dudas sobre Git y control de versiones\n" +
            "- Ejecutar operaciones del asistente Chapi\n\n" +
            "Cuentame, que necesitas hacer hoy?",
            showAlert: false);
    }

    public async Task UpdateProjectContextAsync(string projectPath, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        bool isNewProject = projectPath != _currentProjectPath;
        if (!isNewProject && !forceRefresh)
        {
            return;
        }

        _currentProjectPath = projectPath;
        await _conversationManager.UpdateProjectContextAsync(projectPath);

        if (isNewProject)
        {
            var projectContext = _conversationManager.GetCurrentProjectContext();
            if (projectContext != null)
            {
                AddAssistantMessage(
                    $"Proyecto actualizado: **{projectContext.Name}**\nTecnologia: {projectContext.Technology}",
                    showAlert: true,
                    variant: AlertVariant.Info,
                    title: "Proyecto");
                ScrollToBottom?.Invoke();
            }
        }

        await RefreshUIAsync();
    }

    private async Task RefreshUIAsync()
    {
        try
        {
            // NOTA: Se comentan estos refrescos redundantes.
            // MainWindow ya se encarga de actualizar los ViewModels al cambiar de proyecto.
            // Forzar el refresco aqui causa bucles de I/O innecesarios con el FileSystemWatcher.

            /*
            var historyVM = App.ServiceProvider.GetService<HistoryViewModel>();
            var changesVM = App.ServiceProvider.GetService<ChangesViewModel>();

            if (historyVM != null) await historyVM.RefreshCommand.ExecuteAsync(null);
            if (changesVM != null) await changesVM.ForceRefreshAsync();

            if (MainWindow.Instance != null)
            {
                await MainWindow.Instance.UpdateProjectStatusesAsync();
            }
            */
        }
        catch (Exception)
        {
        }
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentMessage))
        {
            return;
        }

        var userMessage = CurrentMessage.Trim();
        CurrentMessage = string.Empty;

        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Text = userMessage,
                Author = MessageAuthor.User,
                Timestamp = DateTime.Now
            });
        }

        IsProcessing = true;

        try
        {
            ChatMessage? assistantMessage = null;

            var result = await _conversationManager.ProcessUserMessageAsync(userMessage, token =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (assistantMessage == null)
                    {
                        assistantMessage = new ChatMessage
                        {
                            Author = MessageAuthor.Assistant,
                            Text = token,
                            Timestamp = DateTime.Now
                        };

                        lock (_messagesLock)
                        {
                            Messages.Add(assistantMessage);
                        }
                    }
                    else
                    {
                        assistantMessage.Text += token;
                    }

                    ScrollToBottom?.Invoke();
                });
            });

            lock (_messagesLock)
            {
                if (result.IsSuccess)
                {
                    if (assistantMessage == null && !string.IsNullOrEmpty(result.Data.Text))
                    {
                        assistantMessage = result.Data;
                        Messages.Add(assistantMessage);
                    }
                    else if (assistantMessage != null)
                    {
                        var finalMessage = result.Data;
                        if (finalMessage != null)
                        {
                            assistantMessage.Action = finalMessage.Action;
                        }
                    }
                }
                else
                {
                    if (assistantMessage != null)
                    {
                        assistantMessage.Text += $"\n\nError: {result.Error}";
                    }
                    else
                    {
                        Messages.Add(new ChatMessage
                        {
                            Text = $"Error: {result.Error ?? "Desconocido"}",
                            Author = MessageAuthor.Assistant,
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }

            if (!result.IsSuccess)
            {
                ShowAlert(
                    result.Error ?? "Desconocido",
                    AlertVariant.Error,
                    "Asistente",
                    TimeSpan.FromSeconds(6));
            }
        }
        catch (Exception ex)
        {
            AddAssistantMessage(
                $"Ocurrio un error inesperado: {ex.Message}",
                showAlert: true,
                variant: AlertVariant.Error,
                title: "Asistente");
        }
        finally
        {
            IsProcessing = false;
            ScrollToBottom?.Invoke();
        }
    }

    private async Task ExecuteActionAsync(ChatMessage? message)
    {
        if (message?.Action == null || IsProcessing)
        {
            return;
        }

        var intent = message.Action;
        if (string.IsNullOrEmpty(intent.CapabilityId))
        {
            await ExecuteManualIntentAsync(message);
            return;
        }

        var capability = _capabilityRegistry.GetAllCapabilities().FirstOrDefault(c => c.Id == intent.CapabilityId);
        if (capability == null)
        {
            return;
        }

        IsProcessing = true;
        try
        {
            var useCase = App.ServiceProvider.GetService(capability.TargetUseCaseType);
            if (useCase == null)
            {
                AddAssistantMessage(
                    $"Error: No se pudo encontrar el motor para la accion '{capability.Name}'.",
                    showAlert: true,
                    variant: AlertVariant.Error,
                    title: capability.Name);
                return;
            }

            await DispatchUseCaseAsync(useCase, intent, capability);

            message.Action = null;
            await UpdateProjectContextAsync(_currentProjectPath, forceRefresh: true);
        }
        catch (Exception ex)
        {
            AddAssistantMessage(
                $"Error al ejecutar {capability.Name}: {ex.Message}",
                showAlert: true,
                variant: AlertVariant.Error,
                title: capability.Name);
        }
        finally
        {
            IsProcessing = false;
            ScrollToBottom?.Invoke();
        }
    }

    private async Task DispatchUseCaseAsync(object useCase, UserIntent intent, Domain.Models.Assistant.AssistantCapability capability)
    {
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
                    files.AddRange(context.Git.ModifiedFilePaths);
                    files.AddRange(context.Git.UntrackedFiles);
                }

                var cResult = await commitUC.ExecuteAsync(new CommitRequest
                {
                    ProjectPath = _currentProjectPath,
                    Message = fullMsg,
                    Files = files
                });

                AddAssistantResultMessage(capability.Name, cResult.IsSuccess, cResult.Error);

                if (cResult.IsSuccess && shouldPush)
                {
                    AddAssistantMessage("Iniciando push automatico...", showAlert: true, variant: AlertVariant.Info, title: capability.Name);

                    var pushUC = App.ServiceProvider.GetService(typeof(PushChangesUseCase)) as PushChangesUseCase;
                    if (pushUC != null)
                    {
                        var branch = context?.Git?.CurrentBranch ?? "main";
                        var pushRes = await pushUC.ExecuteAsync(_currentProjectPath, branch);
                        AddAssistantMessage(
                            pushRes.IsSuccess ? "Push completado." : $"Error en push: {pushRes.Error}",
                            showAlert: true,
                            variant: pushRes.IsSuccess ? AlertVariant.Success : AlertVariant.Error,
                            title: "Push");
                    }
                }
                break;

            case PushChangesUseCase pushUC:
                var pushBranch = _conversationManager.GetCurrentProjectContext()?.Git?.CurrentBranch ?? "main";
                var pResult = await pushUC.ExecuteAsync(_currentProjectPath, pushBranch);
                AddAssistantResultMessage(capability.Name, pResult.IsSuccess, pResult.Error);
                break;

            case PullChangesUseCase pullUC:
                var pBranch = _conversationManager.GetCurrentProjectContext()?.Git?.CurrentBranch ?? "main";
                var plResult = await pullUC.ExecuteAsync(_currentProjectPath, pBranch, true);
                AddAssistantResultMessage(capability.Name, plResult.IsSuccess, plResult.Error);
                break;

            case StashChangesUseCase stashUC:
                var sMsg = intent.Parameters.GetValueOrDefault("message", "Stash desde el Asistente");
                var sResult = await stashUC.ExecuteAsync(_currentProjectPath, sMsg);
                AddAssistantResultMessage(capability.Name, sResult.IsSuccess, sResult.Error);
                break;

            case StashPopUseCase popUC:
                var stResult = await popUC.ExecuteAsync(_currentProjectPath);
                AddAssistantResultMessage(capability.Name, stResult.IsSuccess, stResult.Error);
                break;

            case ResetCommitUseCase undoUC:
                var uResult = await undoUC.ExecuteAsync(_currentProjectPath, ResetMode.Soft);
                AddAssistantResultMessage(capability.Name, uResult.IsSuccess, uResult.Error);
                break;

            case SwitchBranchUseCase switchUC:
                var swBranch = intent.Parameters.GetValueOrDefault("branch", "");
                if (string.IsNullOrEmpty(swBranch))
                {
                    swBranch = intent.Parameters.GetValueOrDefault("branchName", "");
                }
                var swResult = await switchUC.ExecuteAsync(_currentProjectPath, swBranch);
                AddAssistantResultMessage(capability.Name, swResult.IsSuccess, swResult.Error);
                break;

            case CreateBranchUseCase createBranchUC:
                var newBranch = intent.Parameters.GetValueOrDefault("name", "");
                if (string.IsNullOrEmpty(newBranch))
                {
                    newBranch = intent.Parameters.GetValueOrDefault("branchName", "");
                }
                var cbResult = await createBranchUC.ExecuteAsync(_currentProjectPath, newBranch);
                AddAssistantResultMessage(capability.Name, cbResult.IsSuccess, cbResult.Error);
                break;

            case CreateTagUseCase createTagUC:
                var tagName = intent.Parameters.GetValueOrDefault("name", "");
                if (string.IsNullOrEmpty(tagName))
                {
                    tagName = intent.Parameters.GetValueOrDefault("tagName", "");
                }
                var tMsg = intent.Parameters.GetValueOrDefault("message", $"Tag {tagName} desde Asistente");
                var ctResult = await createTagUC.ExecuteAsync(_currentProjectPath, tagName, tMsg);
                AddAssistantResultMessage(capability.Name, ctResult.IsSuccess, ctResult.Error);
                break;

            case DiscardChangesUseCase discardUC:
                await discardUC.ExecuteAsync(_currentProjectPath);
                break;

            case FetchChangesUseCase fetchUC:
                var fResult = await fetchUC.ExecuteAsync(_currentProjectPath, isSilent: false);
                AddAssistantResultMessage(capability.Name, fResult.IsSuccess, fResult.Error);
                break;

            case Chapi.Application.UseCases.Projects.CloneProjectUseCase cloneUC:
                if (!intent.Parameters.TryGetValue("url", out var repoUrl) || string.IsNullOrWhiteSpace(repoUrl))
                {
                    AddAssistantMessage("Necesito la URL del repositorio para clonar. Cual es?", showAlert: true, variant: AlertVariant.Warning, title: "Clonar");
                    break;
                }

                if (!intent.Parameters.TryGetValue("path", out var clonePath) || string.IsNullOrWhiteSpace(clonePath))
                {
                    AddAssistantMessage("En que carpeta quieres que clone este repositorio?", showAlert: true, variant: AlertVariant.Warning, title: "Clonar");
                    break;
                }

                AddAssistantMessage($"Clonando desde '{repoUrl}' en '{clonePath}'...", showAlert: true, variant: AlertVariant.Info, title: "Clonar");

                var cloneResult = await cloneUC.ExecuteAsync(repoUrl, clonePath);

                if (cloneResult.IsSuccess)
                {
                    AddAssistantMessage($"Clonado exitoso en {cloneResult.Data}. Cambiando contexto...", showAlert: true, variant: AlertVariant.Success, title: "Clonar");
                    await UpdateProjectContextAsync(cloneResult.Data, true);
                }
                else
                {
                    AddAssistantMessage($"Error al clonar: {cloneResult.Error}", showAlert: true, variant: AlertVariant.Error, title: "Clonar");
                }
                break;

            case Chapi.Application.UseCases.Projects.CreateProjectUseCase createUC:
                if (!intent.Parameters.TryGetValue("name", out var pName) || string.IsNullOrWhiteSpace(pName))
                {
                    AddAssistantMessage("Para crear el proyecto necesito un nombre. Como quieres llamarlo?", showAlert: true, variant: AlertVariant.Warning, title: "Crear proyecto");
                    break;
                }

                if (!intent.Parameters.TryGetValue("path", out var pPath) || string.IsNullOrWhiteSpace(pPath))
                {
                    AddAssistantMessage("En que carpeta quieres que cree el proyecto?", showAlert: true, variant: AlertVariant.Warning, title: "Crear proyecto");
                    break;
                }

                var pTemplate = intent.Parameters.GetValueOrDefault("template", "https://github.com/Start-Z/CleanArchitecture-Template.git");

                AddAssistantMessage($"Creando proyecto '{pName}' en '{pPath}' usando plantilla Clean Architecture...", showAlert: true, variant: AlertVariant.Info, title: "Crear proyecto");

                var createReq = new Chapi.Application.UseCases.Projects.CreateProjectRequest(pName, pPath, pTemplate);
                var createResult = await createUC.ExecuteAsync(createReq);

                if (createResult.IsSuccess)
                {
                    AddAssistantMessage($"Proyecto creado en {createResult.Data}. Cambiando contexto...", showAlert: true, variant: AlertVariant.Success, title: "Crear proyecto");
                    await UpdateProjectContextAsync(createResult.Data, true);
                }
                else
                {
                    AddAssistantMessage($"Error al crear proyecto: {createResult.Error}", showAlert: true, variant: AlertVariant.Error, title: "Crear proyecto");
                }
                break;

            case Chapi.Application.UseCases.Projects.LoadProjectsUseCase listUC:
                var listResult = await listUC.ExecuteAsync();
                if (listResult.IsSuccess)
                {
                    var fileList = string.Join("\n", listResult.Data.Select(p => $"- {p.Name} ({p.FullPath})"));
                    AddAssistantMessage($"**Mis Proyectos**:\n{fileList}", showAlert: true, variant: AlertVariant.Info, title: "Proyectos");
                }
                else
                {
                    AddAssistantMessage($"Error al listar: {listResult.Error}", showAlert: true, variant: AlertVariant.Error, title: "Proyectos");
                }
                break;

            case Chapi.Application.UseCases.Projects.AddProjectUseCase addProjectUC:
                var addPath = intent.Parameters.GetValueOrDefault("path", "");
                if (string.IsNullOrEmpty(addPath))
                {
                    AddAssistantMessage("Por favor indica la ruta de la carpeta.", showAlert: true, variant: AlertVariant.Warning, title: "Agregar proyecto");
                    break;
                }
                var addResult = await addProjectUC.ExecuteAsync(addPath);
                AddAssistantMessage(
                    addResult.IsSuccess ? "Proyecto agregado a la lista." : $"Error: {addResult.Error}",
                    showAlert: true,
                    variant: addResult.IsSuccess ? AlertVariant.Success : AlertVariant.Error,
                    title: "Agregar proyecto");
                break;

            case Chapi.Application.UseCases.Projects.RemoveProjectUseCase removeProjectUC:
                var remPath = intent.Parameters.GetValueOrDefault("path", _currentProjectPath);
                var remResult = await removeProjectUC.ExecuteAsync(remPath);
                AddAssistantMessage(
                    remResult.IsSuccess ? "Proyecto removido de la lista." : $"Error: {remResult.Error}",
                    showAlert: true,
                    variant: remResult.IsSuccess ? AlertVariant.Success : AlertVariant.Error,
                    title: "Eliminar proyecto");
                break;

            default:
                AddAssistantMessage(
                    $"La capacidad '{capability.Name}' esta registrada pero el despachador no sabe como llamarla.",
                    showAlert: true,
                    variant: AlertVariant.Warning,
                    title: capability.Name);
                break;
        }
    }

    private async Task ExecuteManualIntentAsync(ChatMessage message)
    {
        AddAssistantMessage(
            "Esta accion requiere ser mapeada al nuevo sistema de capacidades.",
            showAlert: true,
            variant: AlertVariant.Warning,
            title: "Asistente");
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

    private void AddAssistantResultMessage(string capabilityName, bool isSuccess, string? error)
    {
        AddAssistantMessage(
            isSuccess ? $"{capabilityName} completado." : $"Error: {error}",
            showAlert: true,
            variant: isSuccess ? AlertVariant.Success : AlertVariant.Error,
            title: capabilityName);
    }

    private void AddAssistantMessage(string text, bool showAlert, AlertVariant? variant = null, string? title = null)
    {
        lock (_messagesLock)
        {
            Messages.Add(new ChatMessage
            {
                Text = text,
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            });
        }

        if (showAlert)
        {
            ShowAlert(text, variant ?? InferAlertVariant(text), title);
        }
    }

    private void ShowAlert(string text, AlertVariant variant, string? title = null, TimeSpan? duration = null)
    {
        _alertService.Show(
            text,
            title ?? GetAlertTitle(variant),
            variant,
            duration: duration ?? (variant == AlertVariant.Error ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(4)));
    }

    private static AlertVariant InferAlertVariant(string text)
    {
        if (text.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Error;
        }

        if (text.Contains("necesito", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("por favor", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Warning;
        }

        if (text.Contains("completado", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("exitoso", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Success;
        }

        return AlertVariant.Info;
    }

    private static string GetAlertTitle(AlertVariant variant) => variant switch
    {
        AlertVariant.Success => "Correcto",
        AlertVariant.Warning => "Aviso",
        AlertVariant.Error => "Error",
        _ => "Informacion"
    };
}
