using Chapi.Domain.Enums;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Chapi.Domain.Entities.Workspace;

public class WorkspaceTask : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;
    private bool _isInProgress;
    private bool _isDeleted;
    private DateTime? _deletedAt;
    private TaskPriority _priority = TaskPriority.Media;
    private DateTime _updatedAt = DateTime.Now;
    private DateTime? _completedAt;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                OnPropertyChanged();
                if (_isCompleted && _isInProgress)
                {
                    _isInProgress = false;
                    OnPropertyChanged(nameof(IsInProgress));
                }

                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public bool IsInProgress
    {
        get => _isInProgress;
        set
        {
            var normalizedValue = _isCompleted ? false : value;
            if (_isInProgress != normalizedValue)
            {
                _isInProgress = normalizedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public TaskPriority Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (_updatedAt != value)
            {
                _updatedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (_completedAt != value)
            {
                _completedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public WorkspaceTaskStatus Status
    {
        get => IsCompleted
            ? WorkspaceTaskStatus.Completada
            : IsInProgress
                ? WorkspaceTaskStatus.EnCurso
                : WorkspaceTaskStatus.NoIniciada;
        set
        {
            switch (value)
            {
                case WorkspaceTaskStatus.Completada:
                    IsCompleted = true;
                    IsInProgress = false;
                    break;
                case WorkspaceTaskStatus.EnCurso:
                    IsCompleted = false;
                    IsInProgress = true;
                    break;
                default:
                    IsCompleted = false;
                    IsInProgress = false;
                    break;
            }
        }
    }

    // Soft Delete
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted != value)
            {
                _isDeleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldBePermanentlyDeleted));
            }
        }
    }

    public DateTime? DeletedAt
    {
        get => _deletedAt;
        set
        {
            if (_deletedAt != value)
            {
                _deletedAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DaysSinceDeletion));
                OnPropertyChanged(nameof(DaysRemaining));
            }
        }
    }

    public int DaysSinceDeletion => DeletedAt.HasValue
        ? (DateTime.Now - DeletedAt.Value).Days
        : 0;

    public int DaysRemaining => 60 - DaysSinceDeletion;

    public bool ShouldBePermanentlyDeleted => IsDeleted && DaysSinceDeletion >= 60;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
