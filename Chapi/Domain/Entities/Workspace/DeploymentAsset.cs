using System;
using System.IO;

namespace Chapi.Domain.Entities.Workspace;

public class DeploymentAsset : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isPending = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = string.Empty;
    
    public bool IsPending 
    { 
        get => _isPending;
        set
        {
            if (_isPending != value)
            {
                _isPending = value;
                OnPropertyChanged();
            }
        }
    }
    
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string FileName => !string.IsNullOrEmpty(FilePath) 
        ? Path.GetFileName(FilePath) 
        : string.Empty;

    public string Extension => !string.IsNullOrEmpty(FilePath)
        ? Path.GetExtension(FilePath).ToLower()
        : string.Empty;

    public bool Exists => File.Exists(FilePath);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
