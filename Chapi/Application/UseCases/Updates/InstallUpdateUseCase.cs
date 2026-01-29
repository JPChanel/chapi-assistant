using Chapi.Domain.Common;
using Velopack;

namespace Chapi.Application.UseCases.Updates;

public class InstallUpdateUseCase
{
    private readonly UpdateManager? _updateManager;

    public InstallUpdateUseCase()
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

    public Result Execute(UpdateInfo updateInfo)
    {
        try
        {
            if (_updateManager == null)
                return Result.Fail("El gestor de actualizaciones no está disponible");

            if (updateInfo == null)
                return Result.Fail("La información de actualización no puede ser nula");

            // Esto reiniciará la aplicación
            _updateManager.ApplyUpdatesAndRestart(updateInfo);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error instalando actualización: {ex.Message}");
        }
    }
}
