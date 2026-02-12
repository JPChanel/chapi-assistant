using Chapi.Application.UseCases.Auth;
using System.Diagnostics;
using System.Windows.Input;

namespace Chapi.Presentation.ViewModels;

public class LoginGitHubViewModel : ViewModelBase
{
    private readonly LoginGitHubUseCase _loginUseCase;
    private string _userCode = string.Empty;
    private string _statusMessage = "Haz clic para conectar con GitHub";
    private bool _isWaiting = false;
    private bool _isLoggedIn = false;
    private CancellationTokenSource? _pollCts;

    public LoginGitHubViewModel(LoginGitHubUseCase loginUseCase)
    {
        _loginUseCase = loginUseCase;
        LoginCommand = new AsyncRelayCommand(_ => StartLoginAsync(), _ => !_isWaiting);
        CancelCommand = new RelayCommand(_ => CancelLogin(), _ => _isWaiting);
    }

    public string UserCode
    {
        get => _userCode;
        set => SetProperty(ref _userCode, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsWaiting
    {
        get => _isWaiting;
        set => SetProperty(ref _isWaiting, value);
    }

    public ICommand LoginCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task StartLoginAsync()
    {
        IsWaiting = true;
        StatusMessage = "Solicitando códigos...";

        var result = await _loginUseCase.RequestCodesAsync();

        if (!result.IsSuccess || result.Data == null)
        {
            StatusMessage = $"Error: {result.Error}";
            IsWaiting = false;
            return;
        }

        var deviceCode = result.Data;
        UserCode = deviceCode.UserCode;
        StatusMessage = "Ingresa el código en tu navegador para autorizar la aplicación.";

        try
        {
            Process.Start(new ProcessStartInfo(deviceCode.VerificationUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = "No se pudo abrir el navegador. Ve a: " + deviceCode.VerificationUri;
        }

        _pollCts = new CancellationTokenSource();
        await PollForToken(deviceCode.DeviceCode, deviceCode.Interval);
    }

    private async Task PollForToken(string deviceCode, int interval)
    {
        int waitTime = interval > 0 ? interval : 5;

        try
        {
            while (_pollCts != null && !_pollCts.Token.IsCancellationRequested)
            {
                // Esperar el intervalo
                await Task.Delay(TimeSpan.FromSeconds(waitTime), _pollCts.Token);

                var result = await _loginUseCase.CompleteLoginAsync(deviceCode, waitTime);

                if (result.IsSuccess)
                {
                    StatusMessage = $"¡Conectado como {result.Data?.Login}!";
                    IsWaiting = false;
                    UserCode = string.Empty;
                    return;
                }

                if (result.Error != "pending")
                {
                    StatusMessage = $"Error: {result.Error}";
                    IsWaiting = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            IsWaiting = false;
        }
    }

    private void CancelLogin()
    {
        _pollCts?.Cancel();
        IsWaiting = false;
        UserCode = string.Empty;
        StatusMessage = "Login cancelado.";
    }
}
