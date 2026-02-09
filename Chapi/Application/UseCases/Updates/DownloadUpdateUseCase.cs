using Chapi.Domain.Common;
using Velopack;

namespace Chapi.Application.UseCases.Updates;

public class DownloadUpdateUseCase
{
    private readonly UpdateManager? _updateManager;

    public DownloadUpdateUseCase()
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

    public async Task<Result> ExecuteAsync(UpdateInfo updateInfo, Action<int>? onProgress = null)
    {
        try
        {
            if (_updateManager == null)
                return Result.Fail("El gestor de actualizaciones no está disponible");

            if (updateInfo == null)
                return Result.Fail("La información de actualización no puede ser nula");

            await _updateManager.DownloadUpdatesAsync(updateInfo, onProgress);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error descargando actualización: {ex.Message}");
        }
    }
}
