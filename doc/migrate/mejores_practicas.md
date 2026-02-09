# 📘 Guía de Mejores Prácticas - Chapi Assistant

Esta guía establece los estándares y patrones a seguir durante y después de la refactorización.

---

## 🎯 Principios Fundamentales

### 1. **SOLID siempre**
- **S**ingle Responsibility: Una clase, una razón para cambiar
- **O**pen/Closed: Abierto para extensión, cerrado para modificación
- **L**iskov Substitution: Las implementaciones deben ser intercambiables
- **I**nterface Segregation: Interfaces pequeñas y específicas
- **D**ependency Inversion: Depende de abstracciones, no de concreciones

### 2. **DRY (Don't Repeat Yourself)**
- Si copias código, crea una abstracción
- Reutiliza Use Cases y servicios
- Centraliza configuraciones

### 3. **KISS (Keep It Simple, Stupid)**
- Prefiere soluciones simples
- No sobre-ingenierizar
- Código legible > Código "clever"

### 4. **YAGNI (You Aren't Gonna Need It)**
- No implementes funcionalidad "por si acaso"
- Espera a que sea realmente necesaria

---

## 📁 Convenciones de Nombres

### Archivos y Carpetas
```
✅ CORRECTO:
- GitRepository.cs
- CommitChangesUseCase.cs
- ChangesViewModel.cs
- INotificationService.cs

❌ INCORRECTO:
- gitRepo.cs
- commit_use_case.cs
- vmChanges.cs
- NotificationServiceInterface.cs
```

### Clases e Interfaces
```csharp
// ✅ Interfaces: Prefijo 'I' + sustantivo
public interface IGitRepository { }
public interface IDialogService { }

// ✅ Clases: Sustantivo descriptivo
public class GitRepository : IGitRepository { }
public class CommitChangesUseCase { }

// ✅ ViewModels: Sufijo 'ViewModel'
public class ChangesViewModel : ViewModelBase { }

// ✅ Use Cases: Verbo + sustantivo + 'UseCase'
public class LoadHistoryUseCase { }
public class CreateTagUseCase { }
```

### Métodos
```csharp
// ✅ Async: Sufijo 'Async'
public async Task<Result> CommitAsync(string message) { }

// ✅ Verbos descriptivos
public void ValidateInput() { }
public bool CanExecute() { }
public void OnPropertyChanged() { }

// ❌ EVITAR nombres genéricos
public void DoStuff() { } // ❌
public void Process() { }  // ❌
```

### Propiedades
```csharp
// ✅ PascalCase para públicas
public string CommitMessage { get; set; }
public bool IsLoading { get; set; }

// ✅ camelCase con _ para privadas
private string _commitMessage;
private bool _isLoading;
```

---

## 🏗️ Patrones Arquitectónicos

### 1. **Repository Pattern**

```csharp
// ✅ CORRECTO: Repositorio enfocado en persistencia
public interface IGitRepository
{
    Task<IEnumerable<GitCommit>> GetCommitsAsync(string projectPath, int limit);
    Task<Result> SaveCommitAsync(GitCommit commit);
}

// ❌ INCORRECTO: Repositorio con lógica de negocio
public interface IGitRepository
{
    Task<bool> CommitAndPushAsync(); // ❌ Esto es un Use Case
    Task ValidateCommitMessage();    // ❌ Esto es validación
}
```

### 2. **Use Case Pattern**

```csharp
// ✅ Estructura estándar de Use Case
public class CommitChangesUseCase
{
    private readonly IGitRepository _gitRepo;
    private readonly INotificationService _notifications;

    public CommitChangesUseCase(IGitRepository gitRepo, INotificationService notifications)
    {
        _gitRepo = gitRepo;
        _notifications = notifications;
    }

    public async Task<Result<GitCommit>> ExecuteAsync(CommitRequest request)
    {
        // 1. Validar
        var validation = Validate(request);
        if (!validation.IsSuccess)
            return Result<GitCommit>.Fail(validation.Error);

        // 2. Ejecutar lógica de negocio
        var result = await _gitRepo.CommitAsync(request.ProjectPath, request.Message);

        // 3. Efectos secundarios (notificaciones, logging)
        if (result.IsSuccess)
            _notifications.ShowSuccess("Commit exitoso");

        return result;
    }

    private Result Validate(CommitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Result.Fail("Mensaje vacío");
        
        return Result.Success();
    }
}
```

### 3. **MVVM Pattern**

```csharp
// ✅ ViewModel bien estructurado
public class ChangesViewModel : ViewModelBase
{
    // Dependencies
    private readonly LoadChangesUseCase _loadChanges;
    private readonly CommitChangesUseCase _commitChanges;

    // Properties con backing fields
    private string _commitMessage;
    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            SetProperty(ref _commitMessage, value);
            CommitCommand.RaiseCanExecuteChanged(); // Actualizar comando
        }
    }

    // Collections observables
    public ObservableCollection<FileChangeViewModel> Changes { get; }

    // Commands
    public ICommand CommitCommand { get; }
    public ICommand RefreshCommand { get; }

    // Constructor con DI
    public ChangesViewModel(
        LoadChangesUseCase loadChanges,
        CommitChangesUseCase commitChanges)
    {
        _loadChanges = loadChanges;
        _commitChanges = commitChanges;
        
        Changes = new ObservableCollection<FileChangeViewModel>();
        CommitCommand = new RelayCommand(CommitAsync, CanCommit);
        RefreshCommand = new RelayCommand(RefreshAsync);
    }

    // Métodos privados para lógica
    private bool CanCommit() => !string.IsNullOrWhiteSpace(CommitMessage);
    
    private async Task CommitAsync()
    {
        var result = await _commitChanges.ExecuteAsync(new CommitRequest
        {
            Message = CommitMessage,
            Files = Changes.Where(c => c.IsSelected).Select(c => c.FilePath)
        });

        if (result.IsSuccess)
        {
            CommitMessage = string.Empty;
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        var changes = await _loadChanges.ExecuteAsync(CurrentProjectPath);
        Changes.Clear();
        foreach (var change in changes)
            Changes.Add(new FileChangeViewModel(change));
    }
}
```

---

## 🛡️ Manejo de Errores

### 1. **Result Pattern (Recomendado)**

```csharp
// ✅ Usar Result para operaciones que pueden fallar
public async Task<Result<GitCommit>> CommitAsync(string message)
{
    try
    {
        var commit = await _gitRepo.CommitAsync(projectPath, message);
        return Result<GitCommit>.Success(commit);
    }
    catch (GitException ex)
    {
        _logger.Error($"Git error: {ex.Message}");
        return Result<GitCommit>.Fail($"Error de Git: {ex.Message}");
    }
    catch (Exception ex)
    {
        _logger.Error($"Unexpected error: {ex.Message}");
        return Result<GitCommit>.Fail("Error inesperado");
    }
}

// ✅ Consumir Result
var result = await _commitUseCase.ExecuteAsync(request);
if (result.IsSuccess)
{
    // Usar result.Data
}
else
{
    // Mostrar result.Error
    await _dialogs.ShowErrorAsync("Error", result.Error);
}
```

### 2. **Excepciones Personalizadas**

```csharp
// Domain/Exceptions/GitException.cs
public class GitException : Exception
{
    public GitException(string message) : base(message) { }
    public GitException(string message, Exception inner) : base(message, inner) { }
}

public class ProjectNotFoundException : Exception
{
    public string ProjectPath { get; }
    
    public ProjectNotFoundException(string path) 
        : base($"Proyecto no encontrado: {path}")
    {
        ProjectPath = path;
    }
}

// Uso
if (!Directory.Exists(projectPath))
    throw new ProjectNotFoundException(projectPath);
```

### 3. **Validación**

```csharp
// ✅ Validación en Use Cases
private Result ValidateCommitRequest(CommitRequest request)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.ProjectPath))
        errors.Add("Ruta de proyecto inválida");

    if (string.IsNullOrWhiteSpace(request.Message))
        errors.Add("Mensaje de commit vacío");

    if (!request.Files.Any())
        errors.Add("No hay archivos seleccionados");

    if (errors.Any())
        return Result.Fail(string.Join("; ", errors));

    return Result.Success();
}

// ✅ Validación en Entidades
public class GitCommit
{
    public string Hash { get; set; }
    public string Message { get; set; }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Hash) 
            && !string.IsNullOrWhiteSpace(Message);
    }
}
```

---

## 📝 Logging

### 1. **Interface de Logger**

```csharp
// Domain/Interfaces/ILogger.cs
public interface ILogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Error(string message, Exception ex);
    void Debug(string message);
}
```

### 2. **Implementación**

```csharp
// Infrastructure/Logging/FileLogger.cs
public class FileLogger : ILogger
{
    private readonly string _logPath;

    public FileLogger()
    {
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Chapi",
            "Logs",
            $"chapi_{DateTime.Now:yyyyMMdd}.log"
        );
        
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
    }

    public void Info(string message) => Log("INFO", message);
    public void Warning(string message) => Log("WARN", message);
    public void Error(string message) => Log("ERROR", message);
    public void Error(string message, Exception ex) => 
        Log("ERROR", $"{message}\n{ex}");
    public void Debug(string message) => Log("DEBUG", message);

    private void Log(string level, string message)
    {
        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        File.AppendAllText(_logPath, logEntry + Environment.NewLine);
    }
}
```

### 3. **Uso en Use Cases**

```csharp
public class CommitChangesUseCase
{
    private readonly ILogger _logger;

    public async Task<Result> ExecuteAsync(CommitRequest request)
    {
        _logger.Info($"Iniciando commit para proyecto: {request.ProjectPath}");
        
        try
        {
            var result = await _gitRepo.CommitAsync(request.ProjectPath, request.Message);
            
            if (result.IsSuccess)
                _logger.Info($"Commit exitoso: {result.Data.Hash}");
            else
                _logger.Warning($"Commit falló: {result.Error}");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error("Error inesperado en CommitChangesUseCase", ex);
            throw;
        }
    }
}
```

---

## 🧪 Testing Best Practices

### 1. **Estructura de Tests**

```csharp
// Chapi.Tests/UseCases/CommitChangesUseCaseTests.cs
public class CommitChangesUseCaseTests
{
    // Arrange: Setup común
    private readonly Mock<IGitRepository> _mockGitRepo;
    private readonly Mock<INotificationService> _mockNotifications;
    private readonly CommitChangesUseCase _useCase;

    public CommitChangesUseCaseTests()
    {
        _mockGitRepo = new Mock<IGitRepository>();
        _mockNotifications = new Mock<INotificationService>();
        _useCase = new CommitChangesUseCase(_mockGitRepo.Object, _mockNotifications.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_ShouldCommitSuccessfully()
    {
        // Arrange
        var request = CreateValidRequest();
        SetupSuccessfulCommit();

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        VerifyCommitWasCalled();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithInvalidMessage_ShouldFail(string message)
    {
        // Arrange
        var request = new CommitRequest { Message = message };

        // Act
        var result = await _useCase.ExecuteAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("mensaje", result.Error.ToLower());
    }

    // Helper methods
    private CommitRequest CreateValidRequest() => new()
    {
        ProjectPath = "C:\\Test",
        Message = "Test commit",
        Files = new[] { "file.cs" }
    };

    private void SetupSuccessfulCommit()
    {
        _mockGitRepo
            .Setup(r => r.CommitAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GitCommit>.Success(new GitCommit()));
    }

    private void VerifyCommitWasCalled()
    {
        _mockGitRepo.Verify(
            r => r.CommitAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Once
        );
    }
}
```

### 2. **Naming Convention para Tests**

```csharp
// Patrón: MethodName_Scenario_ExpectedBehavior

[Fact]
public async Task ExecuteAsync_WithValidRequest_ShouldCommitSuccessfully() { }

[Fact]
public async Task ExecuteAsync_WithEmptyMessage_ShouldReturnError() { }

[Fact]
public async Task ExecuteAsync_WhenGitFails_ShouldLogError() { }
```

---

## 🎨 WPF/XAML Best Practices

### 1. **Data Binding**

```xml
<!-- ✅ CORRECTO: Binding con notificación de cambios -->
<TextBox Text="{Binding CommitMessage, UpdateSourceTrigger=PropertyChanged}" />

<!-- ✅ Command binding -->
<Button Command="{Binding CommitCommand}" 
        Content="Commit"
        IsEnabled="{Binding CanCommit}" />

<!-- ✅ ItemsSource binding -->
<ListView ItemsSource="{Binding Changes}"
          SelectedItem="{Binding SelectedChange}">
```

### 2. **Converters**

```csharp
// Presentation/Converters/BoolToVisibilityConverter.cs
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

```xml
<!-- Uso en XAML -->
<Window.Resources>
    <converters:BoolToVisibilityConverter x:Key="BoolToVisibility" />
</Window.Resources>

<ProgressBar Visibility="{Binding IsLoading, Converter={StaticResource BoolToVisibility}}" />
```

### 3. **Styles y Templates**

```xml
<!-- ✅ Centralizar estilos en ResourceDictionary -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="Background" Value="#FF6200EE" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="Padding" Value="16,8" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>

</ResourceDictionary>
```

---

## 🔄 Async/Await Best Practices

### 1. **Siempre usar Async hasta el final**

```csharp
// ✅ CORRECTO
public async Task LoadDataAsync()
{
    var data = await _repository.GetDataAsync();
    ProcessData(data);
}

// ❌ INCORRECTO: Bloquear con .Result
public void LoadData()
{
    var data = _repository.GetDataAsync().Result; // ❌ Deadlock!
}
```

### 2. **ConfigureAwait en bibliotecas**

```csharp
// En Use Cases y Repositorios (no UI)
public async Task<Result> ExecuteAsync()
{
    var data = await _repo.GetDataAsync().ConfigureAwait(false);
    return ProcessData(data);
}

// En ViewModels (UI context necesario)
public async Task LoadAsync()
{
    var data = await _useCase.ExecuteAsync(); // No usar ConfigureAwait
    UpdateUI(data); // Necesita UI thread
}
```

### 3. **Cancelación**

```csharp
public class LoadHistoryUseCase
{
    public async Task<HistoryResult> ExecuteAsync(
        HistoryRequest request, 
        CancellationToken cancellationToken = default)
    {
        var commits = await _gitRepo.GetCommitsAsync(
            request.ProjectPath, 
            request.Limit,
            cancellationToken
        );

        cancellationToken.ThrowIfCancellationRequested();

        return new HistoryResult { Commits = commits };
    }
}
```

---

## 📦 Dependency Injection

### 1. **Lifetimes**

```csharp
// Singleton: Una instancia para toda la app
services.AddSingleton<ILogger, FileLogger>();
services.AddSingleton<GitCommandExecutor>();

// Scoped: Una instancia por scope (no muy útil en WPF)
services.AddScoped<IGitRepository, GitRepository>();

// Transient: Nueva instancia cada vez
services.AddTransient<CommitChangesUseCase>();
services.AddTransient<ChangesViewModel>();
```

### 2. **Evitar Service Locator**

```csharp
// ❌ ANTI-PATTERN: Service Locator
public class ChangesViewModel
{
    private readonly IServiceProvider _services;

    public ChangesViewModel(IServiceProvider services)
    {
        _services = services;
    }

    public async Task LoadAsync()
    {
        var useCase = _services.GetService<LoadChangesUseCase>(); // ❌
        await useCase.ExecuteAsync();
    }
}

// ✅ CORRECTO: Constructor Injection
public class ChangesViewModel
{
    private readonly LoadChangesUseCase _loadChanges;

    public ChangesViewModel(LoadChangesUseCase loadChanges)
    {
        _loadChanges = loadChanges;
    }

    public async Task LoadAsync()
    {
        await _loadChanges.ExecuteAsync();
    }
}
```

---

## 🚫 Anti-Patterns a Evitar

### 1. **God Object**
```csharp
// ❌ Una clase que hace TODO
public class MainWindow
{
    public void LoadProjects() { }
    public void CommitChanges() { }
    public void PushToRemote() { }
    public void CreateTag() { }
    public void GenerateModule() { }
    // ... 100 métodos más
}
```

### 2. **Anemic Domain Model**
```csharp
// ❌ Entidades sin comportamiento
public class GitCommit
{
    public string Hash { get; set; }
    public string Message { get; set; }
}

// ✅ Entidades con lógica de dominio
public class GitCommit
{
    public string Hash { get; set; }
    public string Message { get; set; }
    
    public string ShortHash => Hash?.Substring(0, 7) ?? string.Empty;
    public bool IsValid() => !string.IsNullOrWhiteSpace(Hash);
    public bool HasTag(string tagName) => Tags.Contains(tagName);
}
```

### 3. **Magic Strings/Numbers**
```csharp
// ❌ Magic strings
if (status == "M") { }
await Git.EjecutarGit("commit -m \"message\"", path);

// ✅ Constantes
public static class GitStatus
{
    public const string Modified = "M";
    public const string Added = "A";
    public const string Deleted = "D";
}

if (status == GitStatus.Modified) { }
```

---

## 📋 Checklist de Code Review

Antes de hacer commit, verifica:

### Código
- [ ] Sigue principios SOLID
- [ ] No hay código duplicado
- [ ] Nombres descriptivos
- [ ] Métodos <50 líneas
- [ ] Clases <300 líneas
- [ ] Sin magic strings/numbers

### Async/Await
- [ ] Métodos async terminan en 'Async'
- [ ] No hay .Result o .Wait()
- [ ] ConfigureAwait usado apropiadamente

### Testing
- [ ] Tests unitarios agregados
- [ ] Tests pasan
- [ ] Cobertura >70%

### Documentación
- [ ] Comentarios en lógica compleja
- [ ] XML comments en APIs públicas
- [ ] README actualizado si es necesario

### Performance
- [ ] No hay N+1 queries
- [ ] Lazy loading donde corresponde
- [ ] Dispose de recursos

---

## 🎯 Resumen

1. **Sigue SOLID religiosamente**
2. **Una responsabilidad por clase**
3. **Inyección de dependencias siempre**
4. **Result pattern para manejo de errores**
5. **Tests para todo lo importante**
6. **Logging en operaciones críticas**
7. **Async/await correctamente**
8. **Nombres descriptivos**
9. **Code review antes de commit**
10. **Refactoriza constantemente**

---

**La calidad del código es responsabilidad de todos. ¡Mantengamos Chapi limpio y mantenible! 🚀**
