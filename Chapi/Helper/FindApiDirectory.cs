using System.IO;

namespace Chapi.Helper;

public class FindApiDirectory
{
    public static string GetDirectory(string basePath)
    {
        var subdirs = Directory.GetDirectories(basePath);
        foreach (var dir in subdirs)
        {
            var files = Directory.GetFiles(dir);
            if (files.Any(f => Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase)))
            {
                return dir;
            }
        }
        return null;
    }
}
