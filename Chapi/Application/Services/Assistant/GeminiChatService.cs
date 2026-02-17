using Chapi.Domain.Common;
using Chapi.Domain.Entities.Assistant;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Chapi.Application.Services.Assistant;

/// <summary>
/// Servicio de chat inteligente que interpreta intenciones y usa contexto del proyecto
/// </summary>
public class GeminiChatService
{
    private readonly IAssistantCapabilityRegistry _capabilityRegistry;
    private readonly IServiceProvider _serviceProvider;

    public GeminiChatService(IServiceProvider serviceProvider, IAssistantCapabilityRegistry capabilityRegistry)
    {
        _serviceProvider = serviceProvider;
        _capabilityRegistry = capabilityRegistry;
    }

    public async Task<Result<string>> SendMessageAsync(
        string userMessage, 
        ConversationContext context,
        Action<string>? onTokenReceived = null)
    {
        try
        {
            var contextInfo = BuildContextInfo(context);
            var conversationHistory = BuildConversationHistory(context);
            var capabilitiesInfo = BuildCapabilitiesInfo();
            
            // Optimización: Si el prompt es muy largo, indicar brevedad
            var fullPrompt = GetPrompt.ChatAssistant(contextInfo, conversationHistory, capabilitiesInfo, userMessage);
            if (fullPrompt.Length > 20000) 
                fullPrompt += "\n\n(Contexto largo: Responde de forma concisa y directa)";

            // Obtener cliente dinámicamente
            var chatClient = _serviceProvider.GetRequiredService<IChatClient>();
            
            var messages = new List<Microsoft.Extensions.AI.ChatMessage> 
            { 
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, fullPrompt) 
            };
            
            var fullResponseBuilder = new StringBuilder();
            
            // Usar Streaming si hay callback, sino normal (aunque GeminiChatClient ya usa stream interno)
            // Aquí forzamos el uso de la API de streaming de IChatClient para que el callback funcione
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages))
            {
                if (update.Contents.Count > 0 && update.Contents[0] is Microsoft.Extensions.AI.TextContent textContent && textContent.Text != null)
                {
                    var token = textContent.Text;
                    fullResponseBuilder.Append(token);
                    onTokenReceived?.Invoke(token);
                }
            }

            var responseText = fullResponseBuilder.ToString();

            if (string.IsNullOrWhiteSpace(responseText))
                return Result<string>.Fail("No se recibió respuesta de la IA (Stream vacío)");

            return Result<string>.Success(responseText);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error al comunicarse con la IA: {ex.Message}");
        }
    }

    private string BuildContextInfo(ConversationContext context)
    {
        if (context.CurrentProject == null)
            return "⚠️ No hay proyecto seleccionado actualmente";

        var sb = new StringBuilder();
        sb.AppendLine("=== CONTEXTO DEL PROYECTO ACTUAL ===");
        sb.AppendLine($"📁 Proyecto: {context.CurrentProject.Name}");
        sb.AppendLine($"📍 Ruta: {context.CurrentProject.Path}");
        sb.AppendLine($"🔧 Tecnología: {context.CurrentProject.Technology}");
        
        if (context.CurrentProject.MainFolders.Any())
        {
            sb.AppendLine($"📂 Carpetas principales: {string.Join(", ", context.CurrentProject.MainFolders)}");
        }

        if (context.CurrentProject.RecentFiles.Any())
        {
            sb.AppendLine($"📝 Archivos recientes modificados:");
            foreach (var file in context.CurrentProject.RecentFiles.Take(5))
            {
                sb.AppendLine($"   - {file}");
            }
        }

        // Contexto Git
        if (context.CurrentProject.Git != null)
        {
            var git = context.CurrentProject.Git;
            sb.AppendLine();
            sb.AppendLine("=== INFORMACIÓN GIT ===");
            sb.AppendLine($"🌿 Branch actual: {git.CurrentBranch}");
            
            if (git.AheadBy > 0 || git.BehindBy > 0)
            {
                sb.AppendLine($"📊 Sincronización: +{git.AheadBy} commits adelante, -{git.BehindBy} commits atrás");
            }

            if (git.HasUncommittedChanges)
            {
                sb.AppendLine($"⚠️ Cambios sin commitear: {git.ModifiedFiles.Count} modificados, {git.UntrackedFiles.Count} sin rastrear");
                
                if (git.ModifiedFiles.Any())
                {
                    sb.AppendLine("   Archivos modificados:");
                    foreach (var file in git.ModifiedFiles.Take(5))
                    {
                        sb.AppendLine($"   - {file}");
                    }
                }
            }

            if (git.RecentCommits.Any())
            {
                sb.AppendLine($"📜 Últimos commits:");
                foreach (var commit in git.RecentCommits.Take(5))
                {
                    sb.AppendLine($"   [{commit.Sha}] {commit.Message} - {commit.Author} ({commit.Date:dd/MM/yyyy})");
                }
            }
        }

        return sb.ToString();
    }

    private string BuildConversationHistory(ConversationContext context)
    {
        if (!context.ConversationHistory.Any())
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== HISTORIAL DE CONVERSACIÓN ===");
        foreach (var msg in context.ConversationHistory.TakeLast(5))
        {
            var author = msg.Author == MessageAuthor.User ? "Usuario" : "Asistente";
            sb.AppendLine($"{author}: {msg.Text}");
        }

        return sb.ToString();
    }

    private string BuildCapabilitiesInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TUS CAPACIDADES (IDs DE ACCIÓN DISPONIBLES) ===");
        
        foreach (var cap in _capabilityRegistry.GetAllCapabilities())
        {
            sb.AppendLine($"- ID: {cap.Id} | Descripción: {cap.Description}");
        }

        return sb.ToString();
    }
}
