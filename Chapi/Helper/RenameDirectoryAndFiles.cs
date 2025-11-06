
using System.IO;

namespace Chapi.Helper;

public class RenameDirectoryAndFiles
{
    public static void RenombrarRecursivamente(string ruta, string antiguo, string nuevo)
    {
        Msg.Assistant($"Renombrando carpetas ...");
        foreach (var dir in Directory.GetDirectories(ruta, "*", SearchOption.AllDirectories))
        {
            if (dir.Contains(antiguo))
            {
                var nuevoDir = dir.Replace(antiguo, nuevo);
                if (!Directory.Exists(nuevoDir))
                {
                    Directory.Move(dir, nuevoDir);
                }

            }
        }
        Msg.Assistant($"Renombrando archivos ...");
        foreach (var archivo in Directory.GetFiles(ruta, "*.*", SearchOption.AllDirectories))
        {
            string contenido = File.ReadAllText(archivo);
            if (contenido.Contains(antiguo))
            {
                File.WriteAllText(archivo, contenido.Replace(antiguo, nuevo));
                Msg.Assistant($"Actualizado: {archivo}");
            }

            if (Path.GetFileName(archivo).Contains(antiguo))
            {
                var nuevoNombre = Path.Combine(Path.GetDirectoryName(archivo), Path.GetFileName(archivo).Replace(antiguo, nuevo));
                File.Move(archivo, nuevoNombre);

            }
        }
    }
}
