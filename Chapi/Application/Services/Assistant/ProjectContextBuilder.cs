using Chapi.Domain.Common;
using Chapi.Domain.Entities.Assistant;
using LibGit2Sharp;
using System.IO;

namespace Chapi.Application.Services.Assistant;

/// <summary>
/// Construye el contexto completo del proyecto actual para el asistente
/// </summary>
public class ProjectContextBuilder
{
    public async Task<Result<ProjectContext>> BuildContextAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return Result<ProjectContext>.Fail("Ruta de proyecto inválida");

            var context = new ProjectContext
            {
                Name = new DirectoryInfo(projectPath).Name,
                Path = projectPath,
                Technology = await DetectTechnologyAsync(projectPath),
                MainFolders = GetMainFolders(projectPath),
                RecentFiles = GetRecentFiles(projectPath),
                Git = await BuildGitContextAsync(projectPath),
                Capabilities = DetectChapiCapabilities(projectPath)
            };

            return Result<ProjectContext>.Success(context);
        }
        catch (Exception ex)
        {
            return Result<ProjectContext>.Fail($"Error al construir contexto: {ex.Message}");
        }
    }

    private async Task<string> DetectTechnologyAsync(string projectPath)
    {
        var technologies = new List<string>();

        // Detectar .NET
        if (Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            technologies.Add("C# .NET");

        if (Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly).Any())
            technologies.Add("Visual Studio Solution");

        // Detectar Node.js
        if (File.Exists(Path.Combine(projectPath, "package.json")))
            technologies.Add("Node.js");

        // Detectar Python
        if (File.Exists(Path.Combine(projectPath, "requirements.txt")) || 
            File.Exists(Path.Combine(projectPath, "setup.py")))
            technologies.Add("Python");

        // Detectar Java
        if (File.Exists(Path.Combine(projectPath, "pom.xml")))
            technologies.Add("Java Maven");

        if (File.Exists(Path.Combine(projectPath, "build.gradle")))
            technologies.Add("Java Gradle");

        // Detectar frameworks web
        if (File.Exists(Path.Combine(projectPath, "angular.json")))
            technologies.Add("Angular");

        if (Directory.GetDirectories(projectPath, "node_modules", SearchOption.TopDirectoryOnly).Any())
        {
            var packageJson = Path.Combine(projectPath, "package.json");
            if (File.Exists(packageJson))
            {
                var content = await File.ReadAllTextAsync(packageJson);
                if (content.Contains("\"react\"")) technologies.Add("React");
                if (content.Contains("\"vue\"")) technologies.Add("Vue");
                if (content.Contains("\"next\"")) technologies.Add("Next.js");
            }
        }

        return technologies.Any() ? string.Join(", ", technologies) : "Desconocido";
    }

    private List<string> GetMainFolders(string projectPath)
    {
        try
        {
            return Directory.GetDirectories(projectPath, "*", SearchOption.TopDirectoryOnly)
                .Select(d => new DirectoryInfo(d).Name)
                .Where(name => !name.StartsWith(".") && 
                              name != "bin" && 
                              name != "obj" && 
                              name != "node_modules" &&
                              name != "packages")
                .Take(10)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private List<string> GetRecentFiles(string projectPath, int maxFiles = 10)
    {
        try
        {
            var extensions = new[] { ".cs", ".xaml", ".js", ".ts", ".py", ".java", ".cpp", ".h" };
            
            return Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .Where(f => !f.Contains("\\bin\\") && 
                           !f.Contains("\\obj\\") && 
                           !f.Contains("\\node_modules\\") &&
                           !f.Contains("\\.git\\"))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(maxFiles)
                .Select(f => Path.GetRelativePath(projectPath, f))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<GitContext?> BuildGitContextAsync(string projectPath)
    {
        try
        {
            if (!Repository.IsValid(projectPath))
                return null;

            using var repo = new Repository(projectPath);
            var status = repo.RetrieveStatus();

            var gitContext = new GitContext
            {
                CurrentBranch = repo.Head.FriendlyName,
                HasUncommittedChanges = status.IsDirty
            };

            // Commits recientes
            gitContext.RecentCommits = repo.Commits
                .Take(10)
                .Select(c => new CommitInfo
                {
                    Sha = c.Sha[..7],
                    Message = c.MessageShort,
                    Author = c.Author.Name,
                    Date = c.Author.When.DateTime
                })
                .ToList();

            // Archivos modificados (Incluye Staged, Modified, Missing, Renamed, etc.)
            // Excluimos Ignored y Unaltered. Untracked va aparte.
            var dirtyFiles = status
                .Where(s => s.State != FileStatus.Ignored && s.State != FileStatus.Unaltered && s.State != FileStatus.NewInWorkdir)
                .ToList();

            gitContext.ModifiedFiles = dirtyFiles
                .Select(s => $"{s.FilePath} ({s.State})")
                .ToList();

            gitContext.ModifiedFilePaths = dirtyFiles
                .Select(s => s.FilePath)
                .ToList();

            // Archivos untracked (NewInWorkdir)
            gitContext.UntrackedFiles = status.Untracked.Select(u => u.FilePath).ToList();

            // Ahead/Behind
            var trackingBranch = repo.Head.TrackedBranch;
            if (trackingBranch != null)
            {
                var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, trackingBranch.Tip);
                gitContext.AheadBy = divergence?.AheadBy ?? 0;
                gitContext.BehindBy = divergence?.BehindBy ?? 0;
            }

            return gitContext;
        }
        catch
        {
            return null;
        }
    }
    private ChapiCapabilities DetectChapiCapabilities(string projectPath)
    {
        var capabilities = new ChapiCapabilities();

        try
        {
            // Si el proyecto actual es Chapi Assistant o tiene acceso a sus interfaces
            // Aquí detectamos qué servicios están disponibles analizando el código o por estructura
            var domainInterfacesPath = Path.Combine(projectPath, "Chapi", "Domain", "Interfaces");
            var infraServicesPath = Path.Combine(projectPath, "Chapi", "Infrastructure", "Services");

            if (Directory.Exists(domainInterfacesPath))
            {
                var interfaces = Directory.GetFiles(domainInterfacesPath, "I*.cs")
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToList();
                
                capabilities.AvailableServices.AddRange(interfaces!);

                capabilities.CanCommit = interfaces.Contains("IGitRepository");
                capabilities.CanPush = interfaces.Contains("IGitRepository");
                capabilities.CanPull = interfaces.Contains("IGitRepository");
                capabilities.CanCreateBranch = interfaces.Contains("IGitRepository");
                capabilities.CanMergeBranch = interfaces.Contains("IGitRepository");
                capabilities.CanGenerateCode = interfaces.Contains("IChatClient") || interfaces.Contains("ITemplateService");
                capabilities.CanAnalyzeArchitecture = interfaces.Contains("IRoslynService");
            }
            else
            {
                // Si no es el proyecto Chapi, asumimos capacidades básicas del asistente integrado
                capabilities.CanCommit = true;
                capabilities.CanPush = true;
                capabilities.CanPull = true;
                capabilities.CanCreateBranch = true;
                capabilities.CanGenerateCode = true;
            }
        }
        catch
        {
            // Default basic capabilities
            capabilities.CanCommit = true;
            capabilities.CanGenerateCode = true;
        }

        return capabilities;
    }
}
