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

            // Crear mensaje de respuesta
            var assistantMessage = new ChatMessage
            {
                Text = responseResult.Data!,
                Author = MessageAuthor.Assistant,
                Timestamp = DateTime.Now
            };

            _currentContext.ConversationHistory.Add(assistantMessage);

            return Result<ChatMessage>.Success(assistantMessage);
        }
        catch (Exception ex)
        {
            return Result<ChatMessage>.Fail($"Error al procesar mensaje: {ex.Message}");
        }
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
