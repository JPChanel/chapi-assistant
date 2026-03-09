using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chapi.Application.Interfaces;
using Chapi.Domain.Documentation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Chapi.Infrastructure.Documentation;

/// <summary>
/// Sintetiza texto de secciones y cÃ³digo de diagramas usando el proveedor IA configurado.
/// Resuelve IChatClient en cada llamada para respetar cambios de proveedor (OpenAI/Gemini/Claude).
/// </summary>
public class GeminiDocSynthesizer : IDocSynthesizerService
{
    private readonly IServiceProvider _serviceProvider;

    public GeminiDocSynthesizer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<string> GenerateSectionContentAsync(
        string sectionTitle, string projectContext, CancellationToken cancellationToken = default)
    {
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocSection(sectionTitle, projectContext);
        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Messages.FirstOrDefault()?.Text ?? string.Empty;
    }

    public async Task<string> GenerateDiagramCodeAsync(
        string sectionTitle, DiagramFormat format, string projectContext, CancellationToken cancellationToken = default)
    {
        var formatName = format == DiagramFormat.Mermaid ? "Mermaid" : "PlantUML";
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocDiagram(sectionTitle, formatName, projectContext);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var raw = response.Messages.FirstOrDefault()?.Text ?? string.Empty;

        return CleanDiagramCode(raw, format);
    }

    public async Task<Dictionary<string, string>> GenerateMetadataAsync(
        IEnumerable<string> keys, string projectContext, string userPrompt, CancellationToken cancellationToken = default)
    {
        var jsonKeys = string.Join("\n", keys.Select(k => $"- {k}"));
        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocMetadata(jsonKeys, projectContext, userPrompt);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var raw = response.Messages.FirstOrDefault()?.Text ?? "{}";

        try
        {
            var cleanedJson = Regex.Replace(raw, @"^```(json)?|```$", "", RegexOptions.Multiline).Trim();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(cleanedJson);
            return dict ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public async Task<string> AnalyzeProjectContextAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(projectPath))
            return $"Proyecto en: {projectPath}";

        var structure = BuildDirectoryTree(projectPath, depth: 3);
        var configFiles = FindConfigFiles(projectPath);

        var prompt = Chapi.Infrastructure.AI.GetPrompt.DocAnalyzeContext(structure, configFiles);

        var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
        var response = await GetChatClient().GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Messages.FirstOrDefault()?.Text ?? structure;
    }

    private IChatClient GetChatClient() =>
        _serviceProvider.GetRequiredService<IChatClient>();

    private static string BuildDirectoryTree(string path, int depth, string indent = "")
    {
        if (depth == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var dir in Directory.GetDirectories(path)
                .Where(d => !ShouldSkip(Path.GetFileName(d)!))
                .Take(15))
            {
                sb.AppendLine($"{indent}ðŸ“ {Path.GetFileName(dir)}");
                sb.Append(BuildDirectoryTree(dir, depth - 1, indent + "  "));
            }
            foreach (var file in Directory.GetFiles(path).Take(10))
            {
                sb.AppendLine($"{indent}ðŸ“„ {Path.GetFileName(file)}");
            }
        }
        catch { }
        return sb.ToString();
    }

    private static bool ShouldSkip(string name) =>
        name is "bin" or "obj" or "node_modules" or ".git" or ".vs" or "dist" or "build";

    private static string FindConfigFiles(string path)
    {
        var patterns = new[] { "*.csproj", "package.json", "*.sln", "appsettings.json", "*.py", "requirements.txt", "Dockerfile" };
        var found = new List<string>();
        foreach (var pattern in patterns)
        {
            try
            {
                found.AddRange(Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
                    .Take(3)
                    .Select(Path.GetFileName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))!);
            }
            catch { }
        }
        return found.Count > 0 ? string.Join(", ", found.Distinct()) : "No se encontraron archivos de configuraciÃ³n";
    }

    private static string CleanDiagramCode(string raw, DiagramFormat format)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = raw.Split('\n').ToList();
            lines.RemoveAt(0);
            if (lines.LastOrDefault()?.TrimStart().StartsWith("```", StringComparison.Ordinal) == true)
                lines.RemoveAt(lines.Count - 1);
            raw = string.Join('\n', lines).Trim();
        }
        return raw;
    }
}
