using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Infrastructure.Services;

public class ProjectTemplateService : ITemplateService
{
    public async Task<Result> RenameTemplateAsync(string path, string oldName, string newName, Action<string> onProgress = null)
    {
        try
        {
            await Task.Run(() =>
            {
                onProgress?.Invoke("Renombrando carpetas...");
                
                // Renombrar directorios (de abajo hacia arriba para no perder la ruta)
                var directories = Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                                           .OrderByDescending(d => d.Length);

                foreach (var dir in directories)
                {
                    if (Path.GetFileName(dir).Contains(oldName))
                    {
                        var parent = Path.GetDirectoryName(dir);
                        var newDirName = Path.GetFileName(dir).Replace(oldName, newName);
                        var newDirPath = Path.Combine(parent, newDirName);
                        
                        if (!Directory.Exists(newDirPath))
                        {
                            Directory.Move(dir, newDirPath);
                        }
                    }
                }

                onProgress?.Invoke("Renombrando archivos y actualizando contenido...");
                // Volver a obtener archivos porque las rutas de las carpetas pudieron cambiar
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);

                foreach (var archivo in files)
                {
                    // 1. Actualizar contenido
                    try
                    {
                        string contenido = File.ReadAllText(archivo);
                        if (contenido.Contains(oldName))
                        {
                            File.WriteAllText(archivo, contenido.Replace(oldName, newName));
                            onProgress?.Invoke($"Contenido actualizado: {Path.GetFileName(archivo)}");
                        }
                    }
                    catch (IOException) { /* Ignorar archivos binarios o bloqueados */ }

                    // 2. Renombrar archivo
                    if (Path.GetFileName(archivo).Contains(oldName))
                    {
                        var parent = Path.GetDirectoryName(archivo);
                        var nuevoNombre = Path.GetFileName(archivo).Replace(oldName, newName);
                        var nuevoRuta = Path.Combine(parent, nuevoNombre);
                        
                        if (!File.Exists(nuevoRuta))
                        {
                            File.Move(archivo, nuevoRuta);
                        }
                    }
                }
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al renombrar plantilla: {ex.Message}");
        }
    }
}
