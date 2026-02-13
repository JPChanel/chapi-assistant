using Chapi.Domain.Common;
using Chapi.Domain.Entities.Assistant;

namespace Chapi.Application.Services.Assistant;

/// <summary>
/// Gestiona el flujo completo de la conversación: contexto, historial y comunicación con IA
/// </summary>
public class ConversationManager
{
    private readonly ProjectContextBuilder _contextBuilder;
    private readonly GeminiChatService _chatService;
    private ConversationContext _currentContext;

    public ConversationManager()
    {
        _contextBuilder = new ProjectContextBuilder();
        _chatService = new GeminiChatService();
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

            // Obtener respuesta de la IA
            var responseResult = await _chatService.SendMessageAsync(userMessage, _currentContext);

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
                            assistantMessage.Action = new UserIntent
                            {
                                Type = MapIntentType(intentData.Type),
                                Parameters = intentData.Params ?? new Dictionary<string, string>(),
                                OriginalMessage = userMessage
                            };

                            // Limpiar el texto de la respuesta quitando el bloque de acción para que no se vea feo en la UI
                            assistantMessage.Text = aiResponse.Replace($"[[ACTION:{jsonAction}]]", "").Trim();
                        }
                    }
                }
                catch
                {
                    // Si falla el parseo, simplemente no agregamos la acción
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
}
