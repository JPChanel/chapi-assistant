using Chapi.Domain.Common;
using Chapi.Domain.Entities.Assistant;
using Chapi.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Chapi.Application.Services.Assistant;

/// <summary>
/// Gestiona el flujo completo de la conversación: contexto, historial y comunicación con IA
/// </summary>
public class ConversationManager
{
    private readonly ProjectContextBuilder _contextBuilder;
    private readonly GeminiChatService _chatService;
    private readonly IAssistantCapabilityRegistry _capabilityRegistry;
    private ConversationContext _currentContext;

    public ConversationManager()
    {
        _contextBuilder = new ProjectContextBuilder();
        _chatService = new GeminiChatService();
        _capabilityRegistry = App.ServiceProvider.GetRequiredService<IAssistantCapabilityRegistry>();
        _currentContext = new ConversationContext();
    }

    /// <summary>
    /// Actualiza el proyecto actual y reconstruye el contexto
    /// </summary>
    public async Task<Result> UpdateProjectContextAsync(string projectPath)
    {
        var contextResult = await _contextBuilder.BuildContextAsync(projectPath);
        
        if (contextResult.IsSuccess)
        {
            _currentContext.CurrentProject = contextResult.Data;
            return Result.Success();
        }

        return Result.Fail(contextResult.Error);
    }

    /// <summary>
    /// Procesa un mensaje del usuario y obtiene respuesta de la IA
    /// </summary>
    public async Task<Result<ChatMessage>> ProcessUserMessageAsync(string userMessage)
    {
        try
        {
            // Agregar mensaje del usuario al historial
            var userChatMessage = new ChatMessage
            {
                Text = userMessage,
                Author = MessageAuthor.User,
                Timestamp = DateTime.Now
            };

            _currentContext.ConversationHistory.Add(userChatMessage);
            
            // Refrescar estado de Git antes de preguntar a la IA
            await RefreshGitContextAsync();

            // Obtener respuesta de la IA con timeout
            var responseTask = _chatService.SendMessageAsync(userMessage, _currentContext);
            // 90 segundos (1.5 min) de timeout para la IA
            if (await Task.WhenAny(responseTask, Task.Delay(90000)) != responseTask)
            {
                return Result<ChatMessage>.Fail("La IA está tardando demasiado. Verifica tu conexión o intenta de nuevo.");
            }
            var responseResult = await responseTask;

            if (!responseResult.IsSuccess)
                return Result<ChatMessage>.Fail(responseResult.Error);

            var aiResponse = responseResult.Data!;
            var assistantMessage = new ChatMessage
            {
                Text = aiResponse,
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            };

            // Detectar acción en la respuesta [[ACTION:{...}]]
            if (aiResponse.Contains("[[ACTION:"))
            {
                try
                {
                    var start = aiResponse.IndexOf("[[ACTION:") + 9;
                    var end = aiResponse.IndexOf("]]", start);
                    if (end > start)
                    {
                        var jsonAction = aiResponse.Substring(start, end - start);
                        var intentData = System.Text.Json.JsonSerializer.Deserialize<ActionData>(jsonAction, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (intentData != null)
                        {
                            var capability = _capabilityRegistry.GetAllCapabilities()
                                .FirstOrDefault(c => c.Id.Equals(intentData.Type, StringComparison.OrdinalIgnoreCase) || 
                                                   c.Id.EndsWith(intentData.Type, StringComparison.OrdinalIgnoreCase));

                            assistantMessage.Action = new UserIntent
                            {
                                Type = MapIntentType(intentData.Type),
                                CapabilityId = capability?.Id,
                                Parameters = intentData.Params ?? new Dictionary<string, string>(),
                                OriginalMessage = userMessage
                            };

                            assistantMessage.Text = aiResponse.Replace($"[[ACTION:{jsonAction}]]", "").Trim();
                        }
                    }
                }
                catch { }
            }
            else 
            {
                // Si la IA no detectó acción, intentamos identificarla por palabras clave para mayor robustez
                var capability = _capabilityRegistry.FindByIntent(userMessage);
                if (capability != null)
                {
                     assistantMessage.Action = new UserIntent
                     {
                         Type = IntentType.Unknown,
                         CapabilityId = capability.Id,
                         OriginalMessage = userMessage
                     };
                }
            }

            _currentContext.ConversationHistory.Add(assistantMessage);

            return Result<ChatMessage>.Success(assistantMessage);
        }
        catch (Exception ex)
        {
            return Result<ChatMessage>.Fail($"Error al procesar mensaje: {ex.Message}");
        }
    }

    private IntentType MapIntentType(string type)
    {
        return type.ToLower() switch
        {
            "commit" => IntentType.Commit,
            "push" => IntentType.Push,
            "pull" => IntentType.Pull,
            "create_branch" => IntentType.CreateBranch,
            "switch_branch" => IntentType.SwitchBranch,
            _ => IntentType.Unknown
        };
    }

    private class ActionData
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string>? Params { get; set; }
    }

    /// <summary>
    /// Limpia el historial de conversación
    /// </summary>
    public void ClearConversation()
    {
        _currentContext.ConversationHistory.Clear();
        _currentContext.CreatedAt = DateTime.Now;
    }

    /// <summary>
    /// Obtiene el historial completo de la conversación
    /// </summary>
    public List<ChatMessage> GetConversationHistory()
    {
        return _currentContext.ConversationHistory.ToList();
    }

    /// <summary>
    /// Obtiene el contexto actual del proyecto
    /// </summary>
    public ProjectContext? GetCurrentProjectContext()
    {
        return _currentContext.CurrentProject;
    }

    private async Task RefreshGitContextAsync()
    {
        if (_currentContext?.CurrentProject == null) return;
        
        try
        {
            // Envolver en timeout de 2 segundos para no bloquear la UI si Git está lento
            var task = _contextBuilder.BuildGitContextAsync(_currentContext.CurrentProject.Path);
            if (await Task.WhenAny(task, Task.Delay(2000)) == task)
            {
                var gitContext = await task;
                if (gitContext != null)
                {
                    _currentContext.CurrentProject.Git = gitContext;
                }
            }
            else
            {
                // Log timeout (opcional)
                System.Diagnostics.Debug.WriteLine("Git context refresh timed out");
            }
        }
        catch
        {
            // Ignorar errores en refresh silencioso
        }
    }
}
