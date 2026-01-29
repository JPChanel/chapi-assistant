using Chapi.Domain.Common;
using Velopack;

namespace Chapi.Application.UseCases.Updates;

public class CheckForUpdatesUseCase
{
    private readonly UpdateManager? _updateManager;

    public CheckForUpdatesUseCase()
    {
        try
        {
            _updateManager = new UpdateManager("https://github.com/JPChanelPJ/chapi-assistant");
        }
        catch
        {
            _updateManager = null;
        }
    }

    public async Task<Result<UpdateInfo?>> ExecuteAsync()
    {
        try
        {
            if (_updateManager == null)
                return Result<UpdateInfo?>.Fail("El gestor de actualizaciones no está disponible");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            return Result<UpdateInfo?>.Success(updateInfo);
        }
        catch (Exception ex)
        {
            return Result<UpdateInfo?>.Fail($"Error verificando actualizaciones: {ex.Message}");
        }
    }
}
