using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Notifications.Services;
using Chapi.Presentation.Shared.Notifications.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UseCases = Chapi.Application.UseCases.Git;

namespace Chapi.Startup.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChapiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGitInfrastructure(configuration);
        services.AddApplicationServices();
        services.AddPresentationServices();

        return services;
    }

    private static IServiceCollection AddGitInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IGitRepository, Chapi.Infrastructure.Git.GitCliRepository>();
        services.Configure<Chapi.Infrastructure.Configuration.GitAuthConfig>(configuration.GetSection("GitAuth"));

        services.AddSingleton<ICredentialStorageService, WindowsCredentialStorageService>();
        services.AddSingleton<System.Net.Http.HttpClient>();
        services.AddSingleton<Chapi.Infrastructure.Services.Auth.GitHubOAuthProvider>();
        services.AddSingleton<Chapi.Infrastructure.Services.Auth.GitLabOAuthProvider>();
        services.AddSingleton<IGitAuthProviderFactory, Chapi.Infrastructure.Services.Auth.GitAuthProviderFactory>();

        services.AddSingleton<IAlertService, AlertService>();
        services.AddSingleton<INotificationService, MessageNotificationService>();
        services.AddSingleton<IModuleGeneratorService, ModuleGeneratorService>();
        services.AddSingleton<IGitHubAuthService, GitHubAuthService>();
        services.AddSingleton<IAssistantCapabilityRegistry, Chapi.Application.Services.Assistant.AssistantCapabilityRegistry>();

        services.AddTransient<Microsoft.Extensions.AI.IChatClient>(_ =>
        {
            var settings = Chapi.Infrastructure.Persistence.Settings.UserSettingsService.LoadSettings();
            var preferred = (settings.PreferredAiProvider ?? string.Empty).Trim();

            if (preferred.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
                {
                    throw new InvalidOperationException("Proveedor IA = OpenAI, pero falta OpenAI API Key en Configuración > IA.");
                }

                return new Chapi.Infrastructure.AI.OpenAiChatClient(settings.OpenAiApiKey);
            }

            if (preferred.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
                {
                    throw new InvalidOperationException("Proveedor IA = Claude, pero falta Claude API Key en Configuración > IA.");
                }

                return new Chapi.Infrastructure.AI.ClaudeChatClient(settings.ClaudeApiKey);
            }

            if (preferred.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
                {
                    throw new InvalidOperationException("Proveedor IA = Gemini, pero falta Gemini API Key en Configuración > IA.");
                }

                return new Chapi.Infrastructure.AI.GeminiChatClient(settings.GeminiApiKey);
            }

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                throw new InvalidOperationException($"Proveedor IA desconocido: '{preferred}'. Usa Gemini, OpenAI o Claude.");
            }

            if (!string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
            {
                return new Chapi.Infrastructure.AI.OpenAiChatClient(settings.OpenAiApiKey);
            }

            if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                return new Chapi.Infrastructure.AI.GeminiChatClient(settings.GeminiApiKey);
            }

            if (!string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
            {
                return new Chapi.Infrastructure.AI.ClaudeChatClient(settings.ClaudeApiKey);
            }

            throw new InvalidOperationException("No se ha configurado ningún proveedor de IA (Gemini, OpenAI o Claude). Por favor ve a Configuración > IA.");
        });

        services.AddSingleton<ITemplateService, ProjectTemplateService>();
        services.AddSingleton<IProjectRepository, Chapi.Infrastructure.Persistence.Settings.ProjectSettingsRepository>();
        services.AddSingleton<Chapi.Application.Interfaces.Workspace.IWorkspaceService, Chapi.Infrastructure.Services.WorkspaceService>();

        services.AddSingleton<Chapi.Application.Interfaces.IKrokiDiagramService, Chapi.Infrastructure.Documentation.KrokiDiagramService>();
        services.AddSingleton<Chapi.Application.Interfaces.IDocumentPersistenceService, Chapi.Infrastructure.Documentation.AppDataDocPersistenceService>();
        services.AddSingleton<Chapi.Application.Interfaces.IDocumentExportService, Chapi.Infrastructure.Documentation.OpenXmlExportService>();
        services.AddSingleton<Chapi.Application.Interfaces.IDocSynthesizerService, Chapi.Infrastructure.Documentation.GeminiDocSynthesizer>();

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddTransient<UseCases.CommitChangesUseCase>();
        services.AddTransient<UseCases.LoadChangesUseCase>();
        services.AddTransient<UseCases.LoadHistoryUseCase>();
        services.AddTransient<UseCases.LoadReleasesUseCase>();
        services.AddTransient<UseCases.PushChangesUseCase>();
        services.AddTransient<UseCases.PullChangesUseCase>();
        services.AddTransient<UseCases.FetchChangesUseCase>();
        services.AddTransient<UseCases.SwitchBranchUseCase>();
        services.AddTransient<UseCases.GetBranchesUseCase>();
        services.AddTransient<UseCases.StashChangesUseCase>();
        services.AddTransient<UseCases.StashPopUseCase>();
        services.AddTransient<UseCases.StashClearUseCase>();
        services.AddTransient<UseCases.StashDropUseCase>();
        services.AddTransient<UseCases.DiscardChangesUseCase>();
        services.AddTransient<UseCases.ResetCommitUseCase>();
        services.AddTransient<UseCases.CreateBranchUseCase>();
        services.AddTransient<UseCases.CreateTagUseCase>();
        services.AddTransient<UseCases.GetFilesChangedInCommitUseCase>();
        services.AddTransient<UseCases.GetFileDiffUseCase>();
        services.AddTransient<UseCases.AssociateGitUseCase>();
        services.AddTransient<UseCases.DeleteTagUseCase>();
        services.AddTransient<UseCases.GetCommitStatsUseCase>();
        services.AddTransient<UseCases.GetConflictsUseCase>();
        services.AddTransient<UseCases.ResolveConflictUseCase>();

        services.AddTransient<Chapi.Application.UseCases.Projects.AddProjectUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.LoadProjectsUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.RemoveProjectUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.SwitchProjectUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.CreateProjectUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.UpdateProjectIndicatorsUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.CloneProjectUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Projects.DeployProjectReleaseUseCase>();

        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.GenerateModuleUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.GenerateModuleStructureUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApiControllerUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApiEndpointUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApplicationMethodUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddDependencyInjectionUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddDomainMethodUseCase>();
        services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddInfrastructureMethodUseCase>();

        services.AddTransient<Chapi.Application.UseCases.AI.GenerateCommitMessageUseCase>();
        services.AddTransient<Chapi.Application.UseCases.AI.SendChatMessageUseCase>();
        services.AddTransient<Chapi.Application.UseCases.AI.GenerateSqlQueryUseCase>();
        services.AddTransient<Chapi.Application.UseCases.AI.GenerateDocumentSectionUseCase>();
        services.AddTransient<Chapi.Application.UseCases.AI.GenerateAllDocumentSectionsUseCase>();

        services.AddTransient<Chapi.Application.UseCases.Auth.LoginGitHubUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Documentation.ApplyTemplateUseCase>();
        services.AddTransient<Chapi.Application.UseCases.Documentation.ExportDocumentUseCase>();

        services.AddSingleton<Chapi.Application.Services.Assistant.GeminiChatService>();
        services.AddSingleton<Chapi.Application.Services.Assistant.ConversationManager>();

        return services;
    }

    private static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddSingleton<NotificationHostViewModel>();
        services.AddSingleton<Presentation.Shell.Services.ProjectShellService>();
        services.AddSingleton<Presentation.ViewModels.ChangesViewModel>();
        services.AddSingleton<Presentation.ViewModels.HistoryViewModel>();
        services.AddSingleton<Presentation.ViewModels.AssistantViewModel>();
        services.AddSingleton<Presentation.ViewModels.ReleasesViewModel>();
        services.AddSingleton<Presentation.ViewModels.WorkspaceViewModel>();
        services.AddSingleton<Presentation.ViewModels.CloneRepositoryViewModel>();
        services.AddSingleton<Presentation.ViewModels.DocumentationViewModel>();
        services.AddTransient<Presentation.ViewModels.LoginGitHubViewModel>();
        services.AddTransient<Presentation.ViewModels.GitProviderSelectionViewModel>();

        return services;
    }
}
