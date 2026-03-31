using System.Collections.ObjectModel;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Shared.Dialogs.ViewModels;

public class ExecutionLogViewModel : ViewModelBase
{
    private string _title = "Ejecutando...";
    private bool _isRunning = true;
    private bool _isSuccess = false;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set 
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsNotRunning));
            }
        }
    }
    
    public bool IsNotRunning => !_isRunning;

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }

    public ObservableCollection<string> Logs { get; } = new();

    public void AddLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        });
    }

    public void Complete(bool success)
    {
         System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
         {
             IsRunning = false;
             IsSuccess = success;
             AddLog(success ? "✅ PROCESO COMPLETADO EXITOSAMENTE" : "❌ PROCESO FALLIDO");
         });
    }
}
