using Chapi.Domain.Common;
using Chapi.Domain.Entities.Assistant;
using Chapi.Infrastructure.AI;
using System.Text;

namespace Chapi.Application.Services.Assistant;

/// <summary>
/// Servicio de chat inteligente con Gemini que interpreta intenciones y usa contexto del proyecto
/// </summary>
public class GeminiChatService
{
    public async Task<Result<string>> SendMessageAsync(
        string userMessage, 
        ConversationContext context)
    {
        try
        {
            var contextInfo = BuildContextInfo(context);
            var conversationHistory = BuildConversationHistory(context);
            var capabilitiesInfo = BuildCapabilitiesInfo(context);
            
            var fullPrompt = GetPrompt.ChatAssistant(contextInfo, conversationHistory, capabilitiesInfo, userMessage);
            var response = await AIClient.SendPromptAsync(fullPrompt);

            if (string.IsNullOrWhiteSpace(response))
                return Result<string>.Fail("No se recibió respuesta de la IA");

            return Result<string>.Success(response);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error al comunicarse con Gemini: {ex.Message}");
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

    private string BuildCapabilitiesInfo(ConversationContext context)
    {
        if (context.CurrentProject == null)
            return "No hay capacidades detectadas.";

        var caps = context.CurrentProject.Capabilities;
        var sb = new StringBuilder();
        
        if (caps.CanCommit) sb.AppendLine("- Puedo realizar commits locales");
        if (caps.CanPush) sb.AppendLine("- Puedo subir cambios (push)");
        if (caps.CanPull) sb.AppendLine("- Puedo descargar cambios (pull)");
        if (caps.CanCreateBranch) sb.AppendLine("- Puedo crear nuevas ramas (branches)");
        if (caps.CanMergeBranch) sb.AppendLine("- Puedo combinar ramas (merge)");
        if (caps.CanGenerateCode) sb.AppendLine("- Puedo generar código fuente basándome en plantillas");
        if (caps.CanAnalyzeArchitecture) sb.AppendLine("- Puedo analizar la arquitectura usando Roslyn");

        if (caps.AvailableServices.Any())
        {
            sb.AppendLine("Servicios disponibles detectados:");
            foreach (var service in caps.AvailableServices.Take(10))
                sb.AppendLine($"  * {service}");
        }

        return sb.ToString();
    }
}
