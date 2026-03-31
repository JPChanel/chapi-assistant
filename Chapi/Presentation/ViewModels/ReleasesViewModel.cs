using Chapi.Application.UseCases.Git;
using Chapi.Application.UseCases.Projects;
using Chapi.Domain.Interfaces; 
using Chapi.Domain.Models;
using Chapi.Presentation.Shared.Dialogs;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Views.Dialogs;
using System.Collections.ObjectModel;
using System.IO;

namespace Chapi.Presentation.ViewModels;

public class ReleasesViewModel : ViewModelBase
{
    private readonly LoadReleasesUseCase _loadReleasesUseCase;
    private readonly CreateTagUseCase _createTagUseCase;
    private readonly DeleteTagUseCase _deleteTagUseCase;
    private readonly GetFilesChangedInCommitUseCase _getFilesChangedUseCase;
    private readonly GetCommitStatsUseCase _getCommitStatsUseCase;
    private readonly DeployProjectReleaseUseCase _deployProjectReleaseUseCase;
    private readonly INotificationService _notificationService; 
    private string _projectPath = string.Empty;
    private bool _isLoading;
    private GitTagItem? _selectedRelease;
    private ObservableCollection<string> _releaseNotes = new();
    private ObservableCollection<string> _releaseFiles = new();
    private int _filesCount;
    private int _additions;
    private int _deletions;

    public ReleasesViewModel(
        LoadReleasesUseCase loadReleasesUseCase,
        CreateTagUseCase createTagUseCase,
        DeleteTagUseCase deleteTagUseCase,
        GetFilesChangedInCommitUseCase getFilesChangedUseCase,
        GetCommitStatsUseCase getCommitStatsUseCase,
        DeployProjectReleaseUseCase deployProjectReleaseUseCase,
        INotificationService notificationService) 
    {
        _loadReleasesUseCase = loadReleasesUseCase;
        _createTagUseCase = createTagUseCase;
        _deleteTagUseCase = deleteTagUseCase;
        _getFilesChangedUseCase = getFilesChangedUseCase;
        _getCommitStatsUseCase = getCommitStatsUseCase;
        _deployProjectReleaseUseCase = deployProjectReleaseUseCase;
        _notificationService = notificationService; // Asignación
        Releases = new ObservableCollection<GitTagItem>();
        LoadReleasesCommand = new AsyncRelayCommand(async _ => await LoadReleasesAsync());
        CreateTagCommand = new AsyncRelayCommand(async _ => await CreateTagAsync());
        DeleteTagCommand = new AsyncRelayCommand(async param => await DeleteTagAsync(param));
        DeployReleaseCommand = new AsyncRelayCommand(async _ => await DeploySelectedReleaseAsync());
    }

    public ObservableCollection<GitTagItem> Releases { get; }

    public ObservableCollection<string> ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value);
    }

    public ObservableCollection<string> ReleaseFiles
    {
        get => _releaseFiles;
        set => SetProperty(ref _releaseFiles, value);
    }

    public int FilesCount
    {
        get => _filesCount;
        set => SetProperty(ref _filesCount, value);
    }

    public int Additions
    {
        get => _additions;
        set => SetProperty(ref _additions, value);
    }

    public int Deletions
    {
        get => _deletions;
        set => SetProperty(ref _deletions, value);
    }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                LoadReleasesAsync().Forget("cargando releases");
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public GitTagItem? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (SetProperty(ref _selectedRelease, value))
            {
                UpdateReleaseDetails();
            }
        }
    }

    public AsyncRelayCommand LoadReleasesCommand { get; }
    public AsyncRelayCommand CreateTagCommand { get; }
    public AsyncRelayCommand DeleteTagCommand { get; }
    public AsyncRelayCommand DeployReleaseCommand { get; }

    public async Task LoadReleasesAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) return;

        IsLoading = true;
        try
        {
            var releases = await _loadReleasesUseCase.ExecuteAsync(ProjectPath);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Releases.Clear();
                foreach (var release in releases)
                {
                    Releases.Add(release);
                }

                if (Releases.Any() && SelectedRelease == null)
                {
                    SelectedRelease = Releases.First();
                }
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateTagAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;

        // 0. Obtener configuración
        var currentConfig = Chapi.Infrastructure.Persistence.Settings.ProjectConfigurations.GetConfig(ProjectPath);
        string defaultAppName = !string.IsNullOrEmpty(currentConfig.Deployment?.AppName) ? currentConfig.Deployment.AppName : Path.GetFileNameWithoutExtension(ProjectPath);
        string defaultPackageId = !string.IsNullOrEmpty(currentConfig.Deployment?.PackageId) ? currentConfig.Deployment.PackageId : defaultAppName.Replace(" ", "");
        string defaultAuthor = !string.IsNullOrEmpty(currentConfig.Deployment?.Author) ? currentConfig.Deployment.Author : "ANC";
        string defaultLocalPath = currentConfig.Deployment?.LocalPath ?? "";
        string defaultFtpUrl = currentConfig.Deployment?.FtpUrl ?? "";
        string defaultFtpUser = "";
        string defaultIconPath = currentConfig.Deployment?.IconPath ?? "";
        string defaultSplashPath = currentConfig.Deployment?.SplashPath ?? "";

        // 1. Mostrar Diálogo de Configuración
        var (confirmed, tagName, message, isRemote, isLocal, buildAppName, buildPackageId, buildAuthor, localPath, ftpUrl, ftpUser, ftpPass, iconPath, splashPath) =
            await DialogService.ShowCreateReleaseDialog(defaultAppName, defaultPackageId, defaultAuthor, defaultLocalPath, defaultFtpUrl, defaultFtpUser, defaultIconPath, defaultSplashPath);

        if (!confirmed || string.IsNullOrWhiteSpace(tagName)) return;

        // FORZAR PUSH: Siempre subir el tag al remoto para garantizar sincronización
        isRemote = true;

        // Esperar a que se cierre completamente el dialogo anterior (Delay aumentado para evitar bloqueo visual)
        await Task.Delay(800);

        IsLoading = true;

        // 2. Preparar Consola de Logs
        var logVm = new Chapi.Presentation.ViewModels.Dialogs.ExecutionLogViewModel();
        logVm.Title = $"Procesando Release {tagName}";
        var view = new Chapi.Presentation.Views.Dialogs.ExecutionLogDialog { DataContext = logVm };

        // 3. Configurar la lógica del proceso
        Func<Task> runReleaseProcess = async () =>
        {
            try
            {
                // Paso A: Build & Deploy
                if (isLocal)
                {
                    logVm.AddLog("🚀 INICIANDO BUILD Y DESPLIEGUE...");
                    var deployResult = await _deployProjectReleaseUseCase.ExecuteAsync(
                        ProjectPath, 
                        tagName, 
                        logVm.AddLog, 
                        buildAppName, 
                        buildPackageId,
                        buildAuthor, 
                        localPath, 
                        ftpUrl, 
                        ftpUser, 
                        ftpPass,
                        iconPath,
                        splashPath
                    );

                    if (!deployResult.IsSuccess)
                    {
                        logVm.AddLog("❌ ABORTANDO: El proceso de build/despliegue falló.");
                        logVm.Complete(false);
                        return;
                    }
                }
                else
                {
                    logVm.AddLog("ℹ️ Build Local omitido.");
                }

                // Paso B: Crear Tag Git
                logVm.AddLog($"🔖 Creando Tag Git: {tagName} (Push Remoto: {isRemote})...");
                var tagResult = await _createTagUseCase.ExecuteAsync(ProjectPath, tagName, message, isRemote);

                if (!tagResult.IsSuccess)
                {
                    logVm.AddLog($"❌ Error al crear Tag: {tagResult.Error}");
                    logVm.Complete(false);
                    return;
                }
                
                if (isRemote) logVm.AddLog("✅ Tag subido al repositorio remoto.");
                else logVm.AddLog("ℹ️ Push remoto omitido.");

                logVm.AddLog("✅ Proceso finalizado exitosamente.");
                
                // Finalizar
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadReleasesAsync());
                logVm.Complete(true);
            }
            catch (Exception ex)
            {
                logVm.AddLog($"🔥 EXCEPCIÓN CRÍTICA: {ex.Message}");
                logVm.Complete(false);
            }
        };

        // Asignar el inicio del proceso al evento Loaded de la vista
        view.Loaded += async (s, e) => 
        {
            try
            {
                await Task.Delay(500); // Dar tiempo al renderizado inicial
                Task.Run(runReleaseProcess).Forget("ejecutando despliegue de release");
            }
            catch(Exception ex) 
            {
                _notificationService.ShowError($"Error al iniciar proceso tarea: {ex.Message}");
            }
        };

        // 4. Mostrar Consola
        try
        {
            // Asignar Owner para que sea modal sobre la ventana principal
            if (System.Windows.Application.Current.MainWindow != null)
            {
                view.Owner = System.Windows.Application.Current.MainWindow;
            }
            
            view.ShowDialog();
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Error al mostrar consola: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }


    public event EventHandler? TagDeleted;

    private async Task DeleteTagAsync(object? parameter)
    {
        string? tagName = parameter switch
        {
            string s => s,
            GitTagItem tag => tag.TagName,
            _ => SelectedRelease?.TagName
        };

        if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(ProjectPath)) return;

        var confirm = await DialogService.ShowConfirmDialog("Eliminar Tag", $"¿Estas seguro de eliminar el tag '{tagName}'?", DialogVariant.Warning, DialogType.Confirm);
        if (!confirm) return;

        var result = await _deleteTagUseCase.ExecuteAsync(ProjectPath, tagName);
        if (result.IsSuccess)
        {
            await LoadReleasesAsync();
            TagDeleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void UpdateReleaseDetails()
    {
        ReleaseNotes.Clear();
        ReleaseFiles.Clear();
        FilesCount = 0;
        Additions = 0;
        Deletions = 0;

        if (SelectedRelease == null) return;

        // Notas de Versión
        var details = !string.IsNullOrWhiteSpace(SelectedRelease.TagMessage)
            ? SelectedRelease.TagMessage
            : SelectedRelease.CommitMessage;

        if (!string.IsNullOrEmpty(details))
        {
            var lines = details.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                ReleaseNotes.Add(line.Trim());
            }
        }

        // Cargar Archivos Modificados
        if (!string.IsNullOrEmpty(ProjectPath) && !string.IsNullOrEmpty(SelectedRelease.CommitHash))
        {
            try
            {
                var files = await _getFilesChangedUseCase.ExecuteAsync(ProjectPath, SelectedRelease.CommitHash);
                foreach (var file in files)
                {
                    ReleaseFiles.Add(file);
                }
                FilesCount = ReleaseFiles.Count;

                // Obtener estadísticas reales (additions/deletions)
                var (adds, dels) = await _getCommitStatsUseCase.ExecuteAsync(ProjectPath, SelectedRelease.CommitHash);
                Additions = adds;
                Deletions = dels;
            }
            catch { }
        }
    }

    private async Task DeploySelectedReleaseAsync()
    {
        if (SelectedRelease == null || string.IsNullOrEmpty(ProjectPath)) return;

        var confirm = await DialogService.ShowConfirmDialog(
            "Despliegue Local",
            $"¿Generar build y desplegar versión '{SelectedRelease.TagName}'?",
            DialogVariant.Info,
            DialogType.Confirm);

        if (confirm)
        {
            IsLoading = true;
            try
            {
                await _deployProjectReleaseUseCase.ExecuteAsync(ProjectPath, SelectedRelease.TagName);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
