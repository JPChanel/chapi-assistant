using Chapi.Application.Interfaces.Workspace;
using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Entities.Workspace;
using Chapi.Domain.Enums;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Workspace.ViewModels;

public class WorkspaceViewModel : ViewModelBase
{
    private sealed class WorkspaceLoadSnapshot
    {
        public List<WorkspaceTask> ActiveTasks { get; init; } = [];
        public List<WorkspaceTask> HistoryTasks { get; init; } = [];
        public List<DeploymentAsset> DeploymentAssets { get; init; } = [];
        public string SessionNotes { get; init; } = string.Empty;
        public bool NeedsCleanupSave { get; init; }
    }

    private readonly IWorkspaceService _workspaceService;
    private string _currentProjectPath;
    private string _newNoteContent;
    private string _newTaskTitle;
    private TaskPriority _newTaskPriority = TaskPriority.Media;
    private string _randomQuote;
    private bool _hasPendingCriticalAssets;
    private DispatcherTimer _autosaveTimer;
    private bool _isHistoryVisible;
    private bool _isAdjustingTaskMetadata;

    public WorkspaceViewModel(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
        Tasks = new ObservableCollection<WorkspaceTask>();
        Tasks.CollectionChanged += Tasks_CollectionChanged;

        HistoryTasks = new ObservableCollection<WorkspaceTask>();
        DeploymentQueue = new ObservableCollection<DeploymentAsset>();

        AddTaskCommand = new AsyncRelayCommand(AddTaskAsync);
        DeleteTaskCommand = new RelayCommand<WorkspaceTask?>(task =>
        {
            if (task != null) DeleteTask(task);
        });
        DeleteForeverCommand = new RelayCommand<WorkspaceTask?>(task =>
        {
            if (task != null) DeleteForever(task);
        });
        RestoreTaskCommand = new RelayCommand<WorkspaceTask?>(task =>
        {
            if (task != null) RestoreTask(task);
        });
        ToggleHistoryCommand = new RelayCommand(() => IsHistoryVisible = !IsHistoryVisible);

        AddAssetCommand = new AsyncRelayCommand<string?>(async path =>
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                await AddAssetAsync(path);
            }
        });
        RemoveAssetCommand = new RelayCommand<DeploymentAsset?>(asset =>
        {
            if (asset != null) RemoveAsset(asset);
        });
        OpenAssetCommand = new RelayCommand<DeploymentAsset?>(asset =>
        {
            if (asset != null) OpenAsset(asset);
        });
        ToggleAssetStatusCommand = new RelayCommand<DeploymentAsset?>(asset =>
        {
            if (asset != null) ToggleAssetStatus(asset);
        });

        ChangePriorityCommand = new RelayCommand<WorkspaceTask?>(task =>
        {
            if (task != null) CyclePriority(task);
        });
        ToggleTaskInProgressCommand = new RelayCommand<WorkspaceTask?>(task =>
        {
            if (task != null) ToggleTaskInProgress(task);
        });

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
    public ICommand ToggleTaskInProgressCommand { get; }

    private bool _isLoading;

    // Progress Tracking
    private double _displayProgressValue;
    public double DisplayProgressValue
    {
        get => _displayProgressValue;
        set => SetProperty(ref _displayProgressValue, value);
    }

    private string _progressText = "0%";
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    private void RecalculateProgress()
    {
        if (Tasks == null || Tasks.Count == 0)
        {
            AnimateProgressTo(0);
            ProgressText = "0%";
            return;
        }

        var completed = Tasks.Count(t => t.IsCompleted);
        var total = Tasks.Count;

        double target = (double)completed / total * 100;
        AnimateProgressTo(target);
        ProgressText = $"{target:0}%";
    }

    private async void AnimateProgressTo(double target)
    {
        // Simple interpolation logic
        while (Math.Abs(DisplayProgressValue - target) > 0.1)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var step = (target - DisplayProgressValue) * 0.1; // 10% step speed
                // Ensure minimum step to update eventually
                if (Math.Abs(step) < 0.1) step = target > DisplayProgressValue ? 0.1 : -0.1;

                DisplayProgressValue += step;

                // Snap if close
                if (Math.Abs(DisplayProgressValue - target) < 0.1) DisplayProgressValue = target;
            });

            await Task.Delay(15); // 60fps-ish
        }
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
                var snapshot = await Task.Run(() => BuildLoadSnapshot(data));

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

                        foreach (var t in snapshot.ActiveTasks)
                        {
                            try
                            {
                                NormalizeTaskState(t);

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
                        foreach (var t in snapshot.HistoryTasks)
                        {
                            try
                            {
                                NormalizeTaskState(t);

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
                        foreach (var d in snapshot.DeploymentAssets)
                            DeploymentQueue.Add(d);

                        SessionNotes = snapshot.SessionNotes;

                        UpdatePendingStatus();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error inicializando Workspace (UI): {ex.Message}\n{ex.StackTrace}");
                    }
                });

                if (snapshot.NeedsCleanupSave)
                {
                    _ = SaveWorkspaceAsync();
                }
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

    private WorkspaceLoadSnapshot BuildLoadSnapshot(WorkspaceData data)
    {
        var activeTasks = new List<WorkspaceTask>();
        var historyTasks = new List<WorkspaceTask>();
        var needsCleanupSave = false;

        foreach (var task in data.Tasks.ToList())
        {
            try
            {
                if (task.ShouldBePermanentlyDeleted)
                {
                    needsCleanupSave = true;
                    continue;
                }

                NormalizeTaskState(task);

                if (task.Id == Guid.Empty)
                {
                    task.Id = Guid.NewGuid();
                }

                if (task.Title == null)
                {
                    task.Title = task.IsDeleted ? "(Historial sin titulo)" : "(Sin titulo recuperado)";
                }

                if (task.IsDeleted)
                {
                    historyTasks.Add(task);
                }
                else
                {
                    activeTasks.Add(task);
                }
            }
            catch
            {
            }
        }

        return new WorkspaceLoadSnapshot
        {
            ActiveTasks = activeTasks.OrderByDescending(x => x.Priority).ToList(),
            HistoryTasks = historyTasks.OrderByDescending(x => x.DeletedAt).ToList(),
            DeploymentAssets = data.DeploymentQueue.ToList(),
            SessionNotes = data.SessionNotes ?? string.Empty,
            NeedsCleanupSave = needsCleanupSave
        };
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
        if (sender is not WorkspaceTask task) return;
        if (_isAdjustingTaskMetadata) return;

        // Recalculate progress if relevant property changes
        if (e.PropertyName == nameof(WorkspaceTask.IsCompleted) || e.PropertyName == nameof(WorkspaceTask.IsDeleted))
        {
            RecalculateProgress();
        }

        if (e.PropertyName != nameof(WorkspaceTask.UpdatedAt) &&
            e.PropertyName != nameof(WorkspaceTask.CompletedAt) &&
            e.PropertyName != nameof(WorkspaceTask.DaysRemaining) &&
            e.PropertyName != nameof(WorkspaceTask.DaysSinceDeletion) &&
            e.PropertyName != nameof(WorkspaceTask.ShouldBePermanentlyDeleted))
        {
            try
            {
                _isAdjustingTaskMetadata = true;
                task.UpdatedAt = DateTime.Now;

                if (task.IsCompleted)
                {
                    task.CompletedAt ??= task.UpdatedAt;
                }
                else if (task.CompletedAt.HasValue)
                {
                    task.CompletedAt = null;
                }
            }
            finally
            {
                _isAdjustingTaskMetadata = false;
            }
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
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
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

    private void ToggleTaskInProgress(WorkspaceTask task)
    {
        if (task == null || task.IsCompleted)
            return;

        task.IsInProgress = !task.IsInProgress;
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

    private void NormalizeTaskState(WorkspaceTask task)
    {
        if (task.CreatedAt == default)
        {
            task.CreatedAt = DateTime.Now;
        }

        if (task.UpdatedAt == default || task.UpdatedAt < task.CreatedAt)
        {
            task.UpdatedAt = task.CreatedAt;
        }

        if (task.CompletedAt.HasValue && !task.IsCompleted)
        {
            task.IsCompleted = true;
        }

        if (task.IsCompleted)
        {
            task.IsInProgress = false;
            task.CompletedAt ??= task.UpdatedAt;
        }
        else if (task.CompletedAt.HasValue)
        {
            task.CompletedAt = null;
        }
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
