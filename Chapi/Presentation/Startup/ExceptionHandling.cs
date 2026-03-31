using Chapi.Infrastructure.Services;
using Chapi.Presentation.Views.Dialogs;
using System.Windows;

namespace Chapi.Startup;

public sealed class ExceptionHandling : IDisposable
{
    private readonly System.Windows.Application _application;
    private bool _isRegistered;

    public ExceptionHandling(System.Windows.Application application)
    {
        _application = application;
    }

    public void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        _application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        _isRegistered = true;
    }

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;
        _isRegistered = false;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ShowAlert(e.Exception);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ShowAlert(e.ExceptionObject as Exception);
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ShowAlert(e.Exception);
        e.SetObserved();
    }

    private static void ShowAlert(Exception? ex)
    {
        if (ex == null || System.Windows.Application.Current == null)
        {
            return;
        }

        var root = ex.GetBaseException();

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await DialogService.ShowConfirmDialog(
                "Error",
                root.Message,
                DialogVariant.Error,
                DialogType.Info);
        });
    }
}
