using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Chapi.Domain.Enums;

namespace Chapi.Domain.Entities.Workspace;

public class WorkspaceTask : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;
    private bool _isDeleted;
    private DateTime? _deletedAt;
    private TaskPriority _priority = TaskPriority.Media;

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
