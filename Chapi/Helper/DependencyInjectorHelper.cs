using System.IO;
using System.Text.RegularExpressions;

namespace Chapi.Helper
{
    public static class DependencyInjectorHelper
    {
        /// <summary>
        /// Inserta las líneas de inyección de servicios (repositorios y app services)
        /// en el archivo DependencyInjection.cs dentro de los métodos ConfigureRepositories y ConfigureAppServices.
        /// Evita duplicados.
        /// </summary>
        public static async Task InjectServices(string basePath, string apiProjectPath, string moduleName)
        {
            // Determinar ruta del archivo DependencyInjection.cs
            string dependencyInjectionPath = Path.Combine(apiProjectPath, "Config", "DependencyInjection.cs");
            if (!File.Exists(dependencyInjectionPath))
            {
                Msg.Assistant($"No se encontró DependencyInjection.cs en: {dependencyInjectionPath}");
                return;
            }

            string content = await File.ReadAllTextAsync(dependencyInjectionPath);

            string repoLine = $@"services.AddScoped<I{moduleName}Repository, {moduleName}Repository>(); 
                services.AddScoped<ISearch{moduleName}Repository, Search{moduleName}Repository>(); 
                services.AddScoped<IFind{moduleName}Repository, Find{moduleName}Repository>();";

            string appLine = $@"
                services.AddScoped<Search{moduleName}>(); 
                services.AddScoped<{moduleName}>(); 
                services.AddScoped<Find{moduleName}>();";

            // Insertar en ConfigureRepositories
            content = InsertIntoMethod(content, "ConfigureRepositories", repoLine);

            // Insertar en ConfigureAppServices
            content = InsertIntoMethod(content, "ConfigureAppServices", appLine);

            await File.WriteAllTextAsync(dependencyInjectionPath, content);
        }

        private static string InsertIntoMethod(string source, string methodName, string insertion)
        {
            // Buscar firma del método
            // Manejar variantes de espacios/tabulaciones
            var methodPattern = $@"public\s+static\s+void\s+{Regex.Escape(methodName)}\s*\(\s*this\s+IServiceCollection\s+services\s*\)";
            var match = Regex.Match(source, methodPattern);
            if (!match.Success)
            {
                Msg.Assistant($"No se encontró el método {methodName} en DependencyInjection.cs");
                return source;
            }

            // Encontrar el bloque de llaves correspondiente (primer par de llaves que sigue a la firma)
            int startIndex = match.Index;
            int braceOpenIndex = source.IndexOf('{', startIndex);
            if (braceOpenIndex == -1) return source;

            // Encontrar cierre correspondiente (balance simple)
            int index = braceOpenIndex + 1;
            int depth = 1;
            while (index < source.Length && depth > 0)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}') depth--;
                index++;
            }
            int braceCloseIndex = index - 1;
            if (braceCloseIndex <= braceOpenIndex) return source;

            string before = source.Substring(0, braceCloseIndex).TrimEnd();
            string after = source.Substring(braceCloseIndex);

            // Si ya contiene la inserción (por ejemplo la interfaz principal), no duplicar
            if (before.Contains(insertion.Trim())) return source;

            // Insertar justo antes del cierre de la llave del método
            string newContent = $"{before}\n    {insertion}\n{after}";
            return newContent;
        }
    }
}
