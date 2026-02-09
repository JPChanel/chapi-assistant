# 💻 Ejemplos Concretos de Refactorización

Este documento muestra ejemplos específicos de cómo refactorizar tu código actual aplicando SOLID.

---

## 📌 Ejemplo 1: Refactorizar Operación de Commit

### ❌ ANTES - MainWindow.xaml.cs (líneas 1566-1615)

```csharp
private async void btnCommit_Click(object sender, RoutedEventArgs e)
{
    if (!ValidateProject()) return;

    var selectedChanges = ChangesListView.Items
        .Cast<GitStatusItem>()
        .Where(c => c.IsSelected)
        .ToList();

    if (!selectedChanges.Any())
    {
        await DialogService.ShowConfirmDialog(
            "Sin archivos",
            "No hay archivos seleccionados para hacer commit.",
            DialogVariant.Warning,
            DialogType.Info
        );
        return;
    }

    string commitMessage = txtCommitMessage.Text?.Trim();
    if (string.IsNullOrWhiteSpace(commitMessage))
    {
        await DialogService.ShowConfirmDialog(
            "Mensaje vacío",
            "Debes escribir un mensaje de commit.",
            DialogVariant.Warning,
            DialogType.Info
        );
        return;
    }

    await RunWithLoading(async () =>
    {
        foreach (var change in selectedChanges)
        {
            await Git.EjecutarGit($"add \"{change.FilePath}\"", projectDirectory);
        }

        var result = await Git.EjecutarGit($"commit -m \"{commitMessage}\"", projectDirectory);

        if (result.Contains("nothing to commit"))
        {
            Msg.Assistant("⚠️ No hay cambios para commitear.");
        }
        else
        {
            Msg.Assistant($"✅ Commit realizado: {commitMessage}");
            txtCommitMessage.Clear();
            await LoadChangesAsync();
            await LoadHistoryAsync();
        }
    });
}
```

**Problemas:**
- ❌ Mezcla validación, lógica de negocio y UI
- ❌ Difícil de testear
- ❌ Acoplamiento directo con Git
- ❌ Responsabilidades mezcladas

---

### ✅ DESPUÉS - Arquitectura Limpia

#### 1. **Domain/Entities/FileChange.cs**
```csharp
namespace Chapi.Domain.Entities;

public class FileChange
{
    public string FilePath { get; set; }
    public ChangeStatus Status { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    
    public bool IsValid() => !string.IsNullOrWhiteSpace(FilePath);
}

public enum ChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Conflict
}
```

#### 2. **Domain/Interfaces/IGitRepository.cs**
```csharp
namespace Chapi.Domain.Interfaces;

public interface IGitRepository
{
    Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files);
    Task<Result<GitCommit>> CommitAsync(string projectPath, string message);
    Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath);
}

public class Result
{
    public bool IsSuccess { get; set; }
    public string Error { get; set; }
    
    public static Result Success() => new() { IsSuccess = true };
    public static Result Fail(string error) => new() { IsSuccess = false, Error = error };
}

public class Result<T> : Result
{
    public T Data { get; set; }
    
    public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public new static Result<T> Fail(string error) => new() { IsSuccess = false, Error = error };
}
```

#### 3. **Application/UseCases/Git/CommitChangesUseCase.cs**
```csharp
namespace Chapi.Application.UseCases.Git;

public class CommitChangesUseCase
{
    private readonly IGitRepository _gitRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger _logger;

    public CommitChangesUseCase(
        IGitRepository gitRepository,
        INotificationService notificationService,
        ILogger logger)
    {
        _gitRepository = gitRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<GitCommit>> ExecuteAsync(CommitRequest request)
    {
        // 1. Validación
        var validationResult = ValidateRequest(request);
        if (!validationResult.IsSuccess)
            return Result<GitCommit>.Fail(validationResult.Error);

        try
        {
            // 2. Stage files
            var stageResult = await _gitRepository.StageFilesAsync(
                request.ProjectPath, 
                request.FilesToCommit
            );

            if (!stageResult.IsSuccess)
            {
                _logger.Error($"Error staging files: {stageResult.Error}");
                return Result<GitCommit>.Fail($"Error al agregar archivos: {stageResult.Error}");
            }

            // 3. Commit
            var commitResult = await _gitRepository.CommitAsync(
                request.ProjectPath, 
                request.Message
            );

            if (!commitResult.IsSuccess)
            {
                _logger.Error($"Error committing: {commitResult.Error}");
                return Result<GitCommit>.Fail($"Error al hacer commit: {commitResult.Error}");
            }

            // 4. Notificar éxito
            _notificationService.ShowSuccess($"✅ Commit realizado: {request.Message}");
            _logger.Info($"Commit successful: {commitResult.Data.Hash}");

            return commitResult;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error in CommitChangesUseCase: {ex.Message}");
            return Result<GitCommit>.Fail($"Error inesperado: {ex.Message}");
        }
    }

    private Result ValidateRequest(CommitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return Result.Fail("Ruta de proyecto inválida");

        if (string.IsNullOrWhiteSpace(request.Message))
            return Result.Fail("El mensaje de commit no puede estar vacío");

        if (!request.FilesToCommit.Any())
            return Result.Fail("No hay archivos seleccionados para commit");

        return Result.Success();
    }
}

public record CommitRequest(
    string ProjectPath,
    string Message,
    IEnumerable<string> FilesToCommit
);
```

#### 4. **Infrastructure/Git/GitRepository.cs**
```csharp
namespace Chapi.Infrastructure.Git;

public class GitRepository : IGitRepository
{
    private readonly GitCommandExecutor _executor;
    private readonly GitOutputParser _parser;

    public GitRepository(GitCommandExecutor executor, GitOutputParser parser)
    {
        _executor = executor;
        _parser = parser;
    }

    public async Task<Result> StageFilesAsync(string projectPath, IEnumerable<string> files)
    {
        try
        {
            foreach (var file in files)
            {
                var result = await _executor.ExecuteAsync($"add \"{file}\"", projectPath);
                if (!result.IsSuccess)
                    return Result.Fail($"Error staging {file}: {result.Error}");
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    public async Task<Result<GitCommit>> CommitAsync(string projectPath, string message)
    {
        try
        {
            var escapedMessage = message.Replace("\"", "\\\"");
            var result = await _executor.ExecuteAsync($"commit -m \"{escapedMessage}\"", projectPath);

            if (!result.IsSuccess)
                return Result<GitCommit>.Fail(result.Error);

            if (result.Output.Contains("nothing to commit"))
                return Result<GitCommit>.Fail("No hay cambios para commitear");

            // Obtener hash del commit recién creado
            var hashResult = await _executor.ExecuteAsync("rev-parse HEAD", projectPath);
            var hash = hashResult.Output.Trim();

            var commit = new GitCommit
            {
                Hash = hash,
                Message = message,
                Author = Environment.UserName,
                Date = DateTime.Now
            };

            return Result<GitCommit>.Success(commit);
        }
        catch (Exception ex)
        {
            return Result<GitCommit>.Fail(ex.Message);
        }
    }

    public async Task<IEnumerable<FileChange>> GetChangesAsync(string projectPath)
    {
        var result = await _executor.ExecuteAsync("status --porcelain -uall", projectPath);
        if (!result.IsSuccess)
            return Enumerable.Empty<FileChange>();

        return _parser.ParseStatusOutput(result.Output);
    }
}
```

#### 5. **Presentation/ViewModels/ChangesViewModel.cs**
```csharp
namespace Chapi.Presentation.ViewModels;

public class ChangesViewModel : ViewModelBase
{
    private readonly CommitChangesUseCase _commitUseCase;
    private readonly LoadChangesUseCase _loadChangesUseCase;
    private readonly IDialogService _dialogService;

    private string _commitMessage;
    private ObservableCollection<FileChangeViewModel> _changes;

    public ChangesViewModel(
        CommitChangesUseCase commitUseCase,
        LoadChangesUseCase loadChangesUseCase,
        IDialogService dialogService)
    {
        _commitUseCase = commitUseCase;
        _loadChangesUseCase = loadChangesUseCase;
        _dialogService = dialogService;

        Changes = new ObservableCollection<FileChangeViewModel>();
        CommitCommand = new RelayCommand(async () => await CommitAsync(), CanCommit);
        RefreshCommand = new RelayCommand(async () => await LoadChangesAsync());
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            _commitMessage = value;
            OnPropertyChanged();
            CommitCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<FileChangeViewModel> Changes
    {
        get => _changes;
        set
        {
            _changes = value;
            OnPropertyChanged();
        }
    }

    public ICommand CommitCommand { get; }
    public ICommand RefreshCommand { get; }

    private bool CanCommit()
    {
        return !string.IsNullOrWhiteSpace(CommitMessage) 
            && Changes.Any(c => c.IsSelected);
    }

    private async Task CommitAsync()
    {
        var selectedFiles = Changes
            .Where(c => c.IsSelected)
            .Select(c => c.FilePath)
            .ToList();

        var request = new CommitRequest(
            ProjectPath: CurrentProjectPath,
            Message: CommitMessage,
            FilesToCommit: selectedFiles
        );

        var result = await _commitUseCase.ExecuteAsync(request);

        if (result.IsSuccess)
        {
            CommitMessage = string.Empty;
            await LoadChangesAsync();
        }
        else
        {
            await _dialogService.ShowErrorAsync("Error", result.Error);
        }
    }

    private async Task LoadChangesAsync()
    {
        var changes = await _loadChangesUseCase.ExecuteAsync(CurrentProjectPath);
        
        Changes.Clear();
        foreach (var change in changes)
        {
            Changes.Add(new FileChangeViewModel(change));
        }
    }
}
```

#### 6. **Presentation/Views/MainWindow.xaml.cs** (SIMPLIFICADO)
```csharp
namespace Chapi.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // ¡Solo 20 líneas en lugar de 3,637!
    // Toda la lógica está en ViewModels y Use Cases
}
```

---

## 📌 Ejemplo 2: Refactorizar Carga de Historial

### ❌ ANTES - MainWindow.xaml.cs (líneas 620-679)

```csharp
private async Task LoadHistoryAsync()
{
    if (!ValidateProject())
    {
        HistoryListView.ItemsSource = null;
        return;
    }
    
    await UpdateBranchIndicatorsAsync();
    string currentBranch = BranchesComboBox.SelectedItem as string;
    HashSet<string> unpushedHashes = new HashSet<string>();

    if (!string.IsNullOrEmpty(currentBranch))
    {
        unpushedHashes = await Git.GetUnpushedCommitHashes(currentBranch, projectDirectory);
    }

    var tagMap = await Git.GetTagCommitMap(projectDirectory);
    const string fieldSeparator = "\x1f";
    const string recordSeparator = "\x1e";

    string logFormat = $"%H{fieldSeparator}%an{fieldSeparator}%ar{fieldSeparator}%s{fieldSeparator}%b{recordSeparator}";
    var logOutput = await Git.EjecutarGit($"log --pretty=format:\"{logFormat}\" -n {_currentHistoryLimit}", projectDirectory);
    var commits = new List<GitLogItem>();

    if (string.IsNullOrWhiteSpace(logOutput))
    {
        HistoryListView.ItemsSource = commits;
        return;
    }
    
    var commitRecords = logOutput.Split(new[] { recordSeparator }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var line in commitRecords)
    {
        var parts = line.Trim().Trim('"').Split(new[] { fieldSeparator }, StringSplitOptions.None);
        if (parts.Length >= 4)
        {
            var hash = parts[0];
            var commit = new GitLogItem
            {
                Hash = hash,
                Author = parts[1],
                RelativeDate = parts[2],
                Message = parts[3],
                Description = parts.Length > 4 ? parts[4].Trim() : string.Empty,
                IsUnpushed = unpushedHashes.Contains(hash)
            };
            var tagEntry = tagMap.Keys.FirstOrDefault(k => k.StartsWith(hash.Substring(0, 7)));
            if (tagEntry != null)
            {
                commit.Tags = tagMap[tagEntry];
            }
            commits.Add(commit);
        }
    }

    HistoryListView.ItemsSource = commits;
}
```

---

### ✅ DESPUÉS - Separación de Responsabilidades

#### 1. **Infrastructure/Git/GitOutputParser.cs**
```csharp
namespace Chapi.Infrastructure.Git;

public class GitOutputParser
{
    private const string FieldSeparator = "\x1f";
    private const string RecordSeparator = "\x1e";

    public IEnumerable<GitCommit> ParseLogOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Enumerable.Empty<GitCommit>();

        var commits = new List<GitCommit>();
        var records = output.Split(new[] { RecordSeparator }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var record in records)
        {
            var commit = ParseCommitRecord(record);
            if (commit != null)
                commits.Add(commit);
        }

        return commits;
    }

    private GitCommit ParseCommitRecord(string record)
    {
        var parts = record.Trim().Trim('"').Split(new[] { FieldSeparator }, StringSplitOptions.None);
        
        if (parts.Length < 4)
            return null;

        return new GitCommit
        {
            Hash = parts[0],
            Author = parts[1],
            RelativeDate = parts[2],
            Message = parts[3],
            Description = parts.Length > 4 ? parts[4].Trim() : string.Empty
        };
    }

    public IEnumerable<FileChange> ParseStatusOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Enumerable.Empty<FileChange>();

        var changes = new List<FileChange>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var regex = new Regex(@"^(?<status>[A-Z\?]{1,2})\s+(?<file>.+)$");

        foreach (var line in lines)
        {
            var match = regex.Match(line.Trim());
            if (match.Success)
            {
                var status = match.Groups["status"].Value.Trim();
                var filePath = match.Groups["file"].Value.Trim().Replace('/', Path.DirectorySeparatorChar).Trim('"');

                changes.Add(new FileChange
                {
                    FilePath = filePath,
                    Status = MapStatus(status)
                });
            }
        }

        return changes;
    }

    private ChangeStatus MapStatus(string status)
    {
        return status.Trim() switch
        {
            "M" => ChangeStatus.Modified,
            "A" => ChangeStatus.Added,
            "D" => ChangeStatus.Deleted,
            "R" => ChangeStatus.Renamed,
            "??" => ChangeStatus.Untracked,
            "UU" or "AU" or "UA" => ChangeStatus.Conflict,
            _ => ChangeStatus.Modified
        };
    }
}
```

#### 2. **Application/UseCases/Git/LoadHistoryUseCase.cs**
```csharp
namespace Chapi.Application.UseCases.Git;

public class LoadHistoryUseCase
{
    private readonly IGitRepository _gitRepository;
    private readonly ILogger _logger;

    public LoadHistoryUseCase(IGitRepository gitRepository, ILogger logger)
    {
        _gitRepository = gitRepository;
        _logger = logger;
    }

    public async Task<HistoryResult> ExecuteAsync(HistoryRequest request)
    {
        try
        {
            // 1. Cargar commits
            var commits = await _gitRepository.GetCommitsAsync(
                request.ProjectPath, 
                request.Limit
            );

            // 2. Obtener commits no pusheados
            var unpushedHashes = await _gitRepository.GetUnpushedCommitsAsync(
                request.ProjectPath, 
                request.CurrentBranch
            );

            // 3. Obtener tags
            var tagMap = await _gitRepository.GetTagsAsync(request.ProjectPath);

            // 4. Enriquecer commits con información adicional
            var enrichedCommits = commits.Select(commit => new CommitViewModel
            {
                Hash = commit.Hash,
                Author = commit.Author,
                RelativeDate = commit.RelativeDate,
                Message = commit.Message,
                Description = commit.Description,
                IsUnpushed = unpushedHashes.Contains(commit.Hash),
                Tags = GetTagsForCommit(commit.Hash, tagMap)
            }).ToList();

            return new HistoryResult
            {
                Commits = enrichedCommits,
                TotalCount = enrichedCommits.Count
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Error loading history: {ex.Message}");
            return new HistoryResult { Commits = new List<CommitViewModel>() };
        }
    }

    private List<string> GetTagsForCommit(string hash, Dictionary<string, List<string>> tagMap)
    {
        var shortHash = hash.Substring(0, 7);
        var tagEntry = tagMap.Keys.FirstOrDefault(k => k.StartsWith(shortHash));
        return tagEntry != null ? tagMap[tagEntry] : new List<string>();
    }
}

public record HistoryRequest(
    string ProjectPath,
    string CurrentBranch,
    int Limit = 50
);

public class HistoryResult
{
    public List<CommitViewModel> Commits { get; set; }
    public int TotalCount { get; set; }
}
```

#### 3. **Presentation/ViewModels/HistoryViewModel.cs**
```csharp
namespace Chapi.Presentation.ViewModels;

public class HistoryViewModel : ViewModelBase
{
    private readonly LoadHistoryUseCase _loadHistoryUseCase;
    private ObservableCollection<CommitViewModel> _commits;
    private int _currentLimit = 50;
    private const int PageSize = 50;

    public HistoryViewModel(LoadHistoryUseCase loadHistoryUseCase)
    {
        _loadHistoryUseCase = loadHistoryUseCase;
        Commits = new ObservableCollection<CommitViewModel>();
        
        LoadMoreCommand = new RelayCommand(async () => await LoadMoreAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
    }

    public ObservableCollection<CommitViewModel> Commits
    {
        get => _commits;
        set
        {
            _commits = value;
            OnPropertyChanged();
        }
    }

    public ICommand LoadMoreCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task LoadAsync(string projectPath, string currentBranch)
    {
        var request = new HistoryRequest(projectPath, currentBranch, _currentLimit);
        var result = await _loadHistoryUseCase.ExecuteAsync(request);

        Commits.Clear();
        foreach (var commit in result.Commits)
        {
            Commits.Add(commit);
        }
    }

    private async Task LoadMoreAsync()
    {
        _currentLimit += PageSize;
        await LoadAsync(CurrentProjectPath, CurrentBranch);
    }

    private async Task RefreshAsync()
    {
        _currentLimit = PageSize;
        await LoadAsync(CurrentProjectPath, CurrentBranch);
    }
}
```

---

## 📌 Ejemplo 3: Dependency Injection Setup

### **App.xaml.cs** - Configuración de DI

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Chapi;

public partial class App : Application
{
    private ServiceProvider _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure - Git
        services.AddSingleton<GitCommandExecutor>();
        services.AddSingleton<GitOutputParser>();
        services.AddScoped<IGitRepository, GitRepository>();

        // Infrastructure - Other
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ILogger, FileLogger>();

        // Application - Use Cases
        services.AddTransient<CommitChangesUseCase>();
        services.AddTransient<LoadChangesUseCase>();
        services.AddTransient<LoadHistoryUseCase>();
        services.AddTransient<PushChangesUseCase>();
        services.AddTransient<PullChangesUseCase>();
        services.AddTransient<CreateTagUseCase>();

        // Presentation - ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ChangesViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<TagsViewModel>();

        // Presentation - Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
```

---

## 📌 Ejemplo 4: Testing

### **Chapi.Tests/UseCases/CommitChangesUseCaseTests.cs**

```csharp
using Xunit;
using Moq;

namespace Chapi.Tests.UseCases;

public class CommitChangesUseCaseTests
{
    private readonly Mock<IGitRepository> _mockGitRepo;
    private readonly Mock<INotificationService> _mockNotifications;
    private readonly Mock<ILogger> _mockLogger;
    private readonly CommitChangesUseCase _useCase;

    public CommitChangesUseCaseTests()
    {
        _mockGitRepo = new Mock<IGitRepository>();
        _mockNotifications = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger>();
        
        _useCase = new CommitChangesUseCase(
            _mockGitRepo.Object,
            _mockNotifications.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ShouldCommitSuccessfully()
    {
        // Arrange
        var request = new CommitRequest(
            ProjectPath: "C:\\Projects\\Test",
            Message: "Test commit",
            FilesToCommit: new[] { "file1.cs", "file2.cs" }
        );

        _mockGitRepo
            .Setup(r => r.StageFilesAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(Result.Success());

        _mockGitRepo
            .Setup(r => r.CommitAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GitCommit>.Success(new GitCommit 
            { 
                Hash = "abc123", 
                Message = "Test commit" 
            }));

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("abc123", result.Data.Hash);
        
        _mockGitRepo.Verify(r => r.StageFilesAsync(
            "C:\\Projects\\Test", 
            It.IsAny<IEnumerable<string>>()), 
            Times.Once);
        
        _mockGitRepo.Verify(r => r.CommitAsync(
            "C:\\Projects\\Test", 
            "Test commit"), 
            Times.Once);
        
        _mockNotifications.Verify(n => n.ShowSuccess(
            It.IsAny<string>()), 
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyMessage_ShouldReturnError()
    {
        // Arrange
        var request = new CommitRequest(
            ProjectPath: "C:\\Projects\\Test",
            Message: "",
            FilesToCommit: new[] { "file1.cs" }
        );

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("mensaje", result.Error.ToLower());
        
        _mockGitRepo.Verify(r => r.CommitAsync(
            It.IsAny<string>(), 
            It.IsAny<string>()), 
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoFiles_ShouldReturnError()
    {
        // Arrange
        var request = new CommitRequest(
            ProjectPath: "C:\\Projects\\Test",
            Message: "Test commit",
            FilesToCommit: Array.Empty<string>()
        );

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("archivos", result.Error.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WhenGitFails_ShouldReturnError()
    {
        // Arrange
        var request = new CommitRequest(
            ProjectPath: "C:\\Projects\\Test",
            Message: "Test commit",
            FilesToCommit: new[] { "file1.cs" }
        );

        _mockGitRepo
            .Setup(r => r.StageFilesAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(Result.Fail("Git error"));

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Git error", result.Error);
        
        _mockLogger.Verify(l => l.Error(It.IsAny<string>()), Times.Once);
    }
}
```

---

## 🎯 Resumen de Beneficios

| Aspecto | Antes | Después |
|---------|-------|---------|
| **Líneas en MainWindow** | 3,637 | ~200 |
| **Testeable** | ❌ No | ✅ Sí (100% cobertura posible) |
| **Reutilizable** | ❌ No | ✅ Use Cases reutilizables |
| **Mantenible** | ❌ Difícil | ✅ Fácil (responsabilidades claras) |
| **Extensible** | ❌ Riesgoso | ✅ Seguro (OCP) |

---

## 🚀 Próximos Pasos

1. Revisar estos ejemplos
2. Decidir por dónde empezar (recomiendo: Commit)
3. Crear branch `refactor/clean-architecture`
4. Implementar gradualmente

¿Listo para empezar? 💪
