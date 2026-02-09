
using System.IO;

namespace Chapi.Infrastructure.Common;

public static class FileHelper
{
    public static void DeleteRollbackFiles()
    {
        string rollbackDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rollbacks");
        if (Directory.Exists(rollbackDir))
        {

            foreach (string file in Directory.GetFiles(rollbackDir))
            {
                File.Delete(file);
            }


            foreach (string dir in Directory.GetDirectories(rollbackDir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

}
