using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Chapi.Application.Interfaces.Workspace;
using Chapi.Domain.Entities.Workspace;
using Chapi.Domain.Enums;
using System.Windows.Threading;
using System.Windows; 

namespace Chapi.Presentation.ViewModels;

public class WorkspaceViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private string _currentProjectPath;
    private string _newNoteContent;
    private string _newTaskTitle;
    private TaskPriority _newTaskPriority = TaskPriority.Media;
    private string _randomQuote;
    private bool _hasPendingCriticalAssets;
    private DispatcherTimer _autosaveTimer;
    private bool _isHistoryVisible;

    public WorkspaceViewModel(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
        Tasks = new ObservableCollection<WorkspaceTask>();
        Tasks.CollectionChanged += Tasks_CollectionChanged;
        
        HistoryTasks = new ObservableCollection<WorkspaceTask>();
        DeploymentQueue = new ObservableCollection<DeploymentAsset>();
        
        AddTaskCommand = new RelayCommand(async _ => await AddTaskAsync());
        DeleteTaskCommand = new RelayCommand(param => DeleteTask((WorkspaceTask)param));
        DeleteForeverCommand = new RelayCommand(param => DeleteForever((WorkspaceTask)param));
        RestoreTaskCommand = new RelayCommand(param => RestoreTask((WorkspaceTask)param));
        ToggleHistoryCommand = new RelayCommand(_ => IsHistoryVisible = !IsHistoryVisible);
        
        AddAssetCommand = new RelayCommand(async path => await AddAssetAsync((string)path));
        RemoveAssetCommand = new RelayCommand(param => RemoveAsset((DeploymentAsset)param));
        OpenAssetCommand = new RelayCommand(param => OpenAsset((DeploymentAsset)param));
        ToggleAssetStatusCommand = new RelayCommand(param => ToggleAssetStatus((DeploymentAsset)param));
        
        ChangePriorityCommand = new RelayCommand(param => CyclePriority((WorkspaceTask)param));
        
        // Autosave for notes (Debounce)
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += async (s, e) => 
        {
            _autosaveTimer.Stop();
            await SaveWorkspaceAsync();
        };
    }

    public ObservableCollection<WorkspaceTask> Tasks { get; }
    public ObservableCollection<WorkspaceTask> HistoryTasks { get; }
    public ObservableCollection<DeploymentAsset> DeploymentQueue { get; }

    // Removed NewTaskTitle/Priority properties as they are handled inline

    public string SessionNotes
    {
        get => _newNoteContent;
        set
        {
            if (SetProperty(ref _newNoteContent, value))
            {
                TriggerAutoSave();
            }
        }
    }

    public string RandomQuote
    {
        get => _randomQuote;
        set => SetProperty(ref _randomQuote, value);
    }

    public bool HasPendingCriticalAssets
    {
        get => _hasPendingCriticalAssets;
        set => SetProperty(ref _hasPendingCriticalAssets, value);
    }
    
    public bool IsHistoryVisible
    {
        get => _isHistoryVisible;
        set => SetProperty(ref _isHistoryVisible, value);
    }

    public ICommand AddTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand DeleteForeverCommand { get; }
    public ICommand RestoreTaskCommand { get; }
    public ICommand ToggleHistoryCommand { get; }
    
    public ICommand AddAssetCommand { get; }
    public ICommand RemoveAssetCommand { get; }
    public ICommand OpenAssetCommand { get; }
    public ICommand ToggleAssetStatusCommand { get; }
    public ICommand ChangePriorityCommand { get; }

    private bool _isLoading;
    
    // Progress Tracking
    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private string _progressText = "0/0";
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }
    
    private void RecalculateProgress()
    {
        if (Tasks == null || Tasks.Count == 0)
        {
            ProgressValue = 0;
            ProgressText = "Sin tareas";
            return;
        }

        var completed = Tasks.Count(t => t.IsCompleted);
        var total = Tasks.Count;
        
        ProgressValue = (double)completed / total * 100;
        ProgressText = $"{completed}/{total} Tareas";
    }

    public async Task InitializeAsync(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath)) return;
        
        try 
        {
            _isLoading = true;
            _currentProjectPath = projectPath;

            var result = await _workspaceService.LoadWorkspaceAsync(projectPath);
            if (result.IsSuccess)
            {
                var data = result.Data!;

                // Ensure we interact with ObservableCollections on the UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    try
                    {
                        // Unsubscribe from old items properly before clearing
                        foreach (var t in Tasks) t.PropertyChanged -= Task_PropertyChanged;
                        Tasks.Clear();

                        foreach (var t in HistoryTasks) t.PropertyChanged -= Task_PropertyChanged;
                        HistoryTasks.Clear();
                        
                        DeploymentQueue.Clear();

                        // Auto-cleanup permanent deletions first
                        var toRemove = data.Tasks.Where(t => t.ShouldBePermanentlyDeleted).ToList();
                        bool needsCleanupSave = toRemove.Any();
                        
                        foreach(var item in toRemove) data.Tasks.Remove(item);

                        // Add Active Tasks
                        foreach (var t in data.Tasks.Where(x => !x.IsDeleted).OrderByDescending(x => x.Priority).ToList())
                        {
                             try 
                             {
                                 // Defensive coding: Ensure UI doesn't crash on bad data
                                 if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
                                 if (t.Title == null) t.Title = "(Sin título recuperado)";
                                 
                                 // Manually subscribe to PropertyChanged because CollectionChanged is ignored during loading
                                 t.PropertyChanged -= Task_PropertyChanged;
                                 t.PropertyChanged += Task_PropertyChanged;

                                 Tasks.Add(t);
                             }
                             catch { }
                        }
                            
                        // Add History Tasks
                        foreach (var t in data.Tasks.Where(x => x.IsDeleted).OrderByDescending(x => x.DeletedAt).ToList())
                        {
                            try
                            {
                                if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
                                if (t.Title == null) t.Title = "(Historial sin título)";
                                
                                // Manually subscribe to PropertyChanged
                                t.PropertyChanged -= Task_PropertyChanged;
                                t.PropertyChanged += Task_PropertyChanged;

                                HistoryTasks.Add(t);
                            }
                            catch { }
                        }

                        // Add Deployment Assets
                        foreach (var d in data.DeploymentQueue.ToList())
                            DeploymentQueue.Add(d);
                        
                        SessionNotes = data.SessionNotes;
                        
                        UpdatePendingStatus();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error inicializando Workspace (UI): {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
            
            var quoteResult = await _workspaceService.GetRandomQuoteAsync();
            if (quoteResult.IsSuccess) RandomQuote = quoteResult.Data;
        }
        catch { }
        finally
        {
            _isLoading = false;
            RecalculateProgress();
        }
    }

    private void Tasks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_isLoading) return;

        try
        {
            if (e.NewItems != null)
            {
                foreach (WorkspaceTask item in e.NewItems)
                {
                    // Ensure we don't double subscribe if this item was moved or re-added?
                    // But usually new instance or fresh add.
                    item.PropertyChanged -= Task_PropertyChanged; // Safety removal
                    item.PropertyChanged += Task_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (WorkspaceTask item in e.OldItems)
                    item.PropertyChanged -= Task_PropertyChanged;
            }
            
            RecalculateProgress();
            TriggerAutoSave();
        }
        catch { }
    }

    private void Task_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading) return;
        
        // Recalculate progress if relevant property changes
        if (e.PropertyName == nameof(WorkspaceTask.IsCompleted) || e.PropertyName == nameof(WorkspaceTask.IsDeleted))
        {
            RecalculateProgress();
        }

        TriggerAutoSave();
    }

    private void TriggerAutoSave()
    {
        if (_isLoading) return; // Ultra-safe check
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async Task AddTaskAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
        {
            var task = new WorkspaceTask
            {
                Title = string.Empty,
                Priority = TaskPriority.Media,
                CreatedAt = DateTime.Now
            };
            
            // This triggers CollectionChanged, which subscribes events and triggers AutoSave
            Tasks.Insert(0, task);
        });
    }

    private void CyclePriority(WorkspaceTask task)
    {
        if (task == null) return;
        
        switch (task.Priority)
        {
            case TaskPriority.Baja:
                task.Priority = TaskPriority.Media;
                break;
            case TaskPriority.Media:
                task.Priority = TaskPriority.Alta;
                break;
            case TaskPriority.Alta:
                task.Priority = TaskPriority.Baja;
                break;
        }
        
        TriggerAutoSave();
    }

    private void DeleteTask(WorkspaceTask task)
    {
        if (task == null) return;
        
        task.IsDeleted = true;
        task.DeletedAt = DateTime.Now;
        
        Tasks.Remove(task);
        HistoryTasks.Insert(0, task);
        
        // Remove event subscription from moved task if using creating new instance? 
        // No, it's the same instance, so PropertyChanged event is still subscribed to Task_PropertyChanged.
        // But History items shouldn't trigger auto-save if we strictly follow logic. 
        // However, if we move it to history, we might want to unsubscribe or keep it.
        // For simplicity/safety, let's leave it. If user edits it in history (if possible), it saves.
        
        SaveWorkspaceAsync();
    }
    
    private void DeleteForever(WorkspaceTask task)
    {
        if (task == null) return;
        
        task.PropertyChanged -= Task_PropertyChanged;
        HistoryTasks.Remove(task);
        SaveWorkspaceAsync();
    }
    
    private void RestoreTask(WorkspaceTask task)
    {
        if (task == null) return;
        
        task.IsDeleted = false;
        task.DeletedAt = null;
        
        HistoryTasks.Remove(task);
        Tasks.Insert(0, task);
        
        SaveWorkspaceAsync();
    }

    private async Task AddAssetAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        
        var asset = new DeploymentAsset { FilePath = path };
        DeploymentQueue.Add(asset);
        UpdatePendingStatus();
        
        await SaveWorkspaceAsync();
    }

    private void RemoveAsset(DeploymentAsset asset)
    {
        if (asset == null) return;
        DeploymentQueue.Remove(asset);
        UpdatePendingStatus();
        SaveWorkspaceAsync();
    }
    
    private void OpenAsset(DeploymentAsset asset)
    {
        if (asset == null) return;
        _workspaceService.OpenFileInExplorer(asset.FilePath);
    }
    
    private void ToggleAssetStatus(DeploymentAsset asset)
    {
        if (asset == null) return;
        
        asset.IsPending = !asset.IsPending;
        UpdatePendingStatus();
        SaveWorkspaceAsync();
    }
    
    private void UpdatePendingStatus()
    {
        HasPendingCriticalAssets = DeploymentQueue.Any(d => d.IsPending);
    }
    
    // Public so View can bind IsPending toggle to save
    public async Task SaveInternalAsync()
    {
        UpdatePendingStatus();
        await SaveWorkspaceAsync();
    }

    private async Task SaveWorkspaceAsync()
    {
        if (string.IsNullOrEmpty(_currentProjectPath)) return;
        if (_isLoading) return; 

        try
        {
            // Capture data on UI thread to ensure thread safety and avoid "Collection modified" errors
            WorkspaceData data = null;
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                data = new WorkspaceData
                {
                    ProjectPath = _currentProjectPath,
                    SessionNotes = SessionNotes,
                    LastUpdated = DateTime.Now
                };

                // Create snapshots of collections
                data.Tasks.AddRange(Tasks.ToList());
                data.Tasks.AddRange(HistoryTasks.ToList());
                data.DeploymentQueue.AddRange(DeploymentQueue.ToList());
            });

            if (data == null) return;

            // Perform I/O in background
            await _workspaceService.SaveWorkspaceAsync(data);
        }
        catch { }
    }
}
