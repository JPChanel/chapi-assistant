using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using System.IO;

namespace Chapi.Infrastructure.Services;

public class ProjectTemplateService : ITemplateService
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp", ".svgz",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".dll", ".exe", ".pdb", ".bin", ".iso", ".apk", ".snk", ".pfx",
        ".mp3", ".wav", ".ogg", ".mp4", ".mov", ".avi", ".mkv",
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    public async Task<Result> RenameTemplateAsync(string path, string oldName, string newName, Action<string>? onProgress = null)
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
                    if (Path.GetFileName(dir).Contains(oldName, StringComparison.Ordinal))
                    {
                        var parent = Path.GetDirectoryName(dir);
                        if (string.IsNullOrEmpty(parent)) continue;

                        var newDirName = Path.GetFileName(dir).Replace(oldName, newName, StringComparison.Ordinal);
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
                    var ext = Path.GetExtension(archivo);

                    // 1. Actualizar contenido solo en archivos no binarios
                    if (!BinaryExtensions.Contains(ext))
                    {
                        try
                        {
                            string contenido = File.ReadAllText(archivo);
                            if (contenido.Contains(oldName, StringComparison.Ordinal))
                            {
                                File.WriteAllText(archivo, contenido.Replace(oldName, newName, StringComparison.Ordinal));
                                onProgress?.Invoke($"Contenido actualizado: {Path.GetFileName(archivo)}");
                            }
                        }
                        catch (IOException) { /* Ignorar archivos binarios o bloqueados */ }
                    }

                    // 2. Renombrar archivo
                    if (Path.GetFileName(archivo).Contains(oldName, StringComparison.Ordinal))
                    {
                        var parent = Path.GetDirectoryName(archivo);
                        if (string.IsNullOrEmpty(parent)) continue;

                        var nuevoNombre = Path.GetFileName(archivo).Replace(oldName, newName, StringComparison.Ordinal);
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
