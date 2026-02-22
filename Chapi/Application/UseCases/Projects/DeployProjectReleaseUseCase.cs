using Chapi.Domain.Common;
using Chapi.Domain.Interfaces;
using FluentFTP;
using System.Diagnostics;
using System.IO;

namespace Chapi.Application.UseCases.Projects;

public class DeployProjectReleaseUseCase
{
    private readonly INotificationService _notificationService;
    private readonly IGitRepository _gitRepository;

    public DeployProjectReleaseUseCase(INotificationService notificationService, IGitRepository gitRepository)
    {
        _notificationService = notificationService;
        _gitRepository = gitRepository;
    }

    public async Task<Result> ExecuteAsync(
        string projectPath,
        string tagName,
        Action<string>? onLog = null, // Log en Tiempo Real
        string? overrideAppName = null,
        string? overridePackageId = null,
        string? overrideAuthor = null,
        string? overrideLocalPath = null,
        string? overrideFtpUrl = null,
        string? overrideFtpUser = null,
        string? overrideFtpPass = null,
        string? overrideIconPath = null,
        string? overrideSplashPath = null)
    {
        void Log(string msg) => onLog?.Invoke(msg);

        Log($"🚀 Iniciando Proceso de Deploy para {tagName}...");

        // --- 1. Verificar Herramientas ---
        if (!await EnsureVelopackInstalled(Log))
        {
            return Result.Fail("Error: La herramienta Velopack (vpk) no está disponible ni se pudo instalar. Revisa el log.");
        }

        // 2. Leer configuración de despliegue desde ProjectConfigurations
        var config = Chapi.Infrastructure.Persistence.Settings.ProjectConfigurations.GetConfig(projectPath);

        // Si no existe, crear objeto base pero no guardar todavía hasta confirmar
        if (config.Deployment == null)
        {
            config.Deployment = new Chapi.Infrastructure.Persistence.Settings.DeploymentConfig { IsEnabled = true };
        }


        // --- LÓGICA DE OVERRIDES DE BUILD ---
        string appName = !string.IsNullOrWhiteSpace(overrideAppName)
            ? overrideAppName
            : (!string.IsNullOrWhiteSpace(config.Deployment.AppName) ? config.Deployment.AppName : Path.GetFileNameWithoutExtension(projectPath));

        string packageId = !string.IsNullOrWhiteSpace(overridePackageId)
            ? overridePackageId
            : (!string.IsNullOrWhiteSpace(config.Deployment.PackageId) ? config.Deployment.PackageId : appName.Replace(" ", ""));

        string author = !string.IsNullOrWhiteSpace(overrideAuthor)
            ? overrideAuthor
            : (!string.IsNullOrWhiteSpace(config.Deployment.Author) ? config.Deployment.Author : "ANC");

        Log($"ℹ️ Configuración Build: App={appName}, ID={packageId}, Autor={author}");

        string iconPath = !string.IsNullOrWhiteSpace(overrideIconPath) ? overrideIconPath : (config.Deployment.IconPath ?? "");
        string splashPath = !string.IsNullOrWhiteSpace(overrideSplashPath) ? overrideSplashPath : (config.Deployment.SplashPath ?? "");

        // --- LÓGICA DE OVERRIDES DE DESTINO ---
        string localPath = !string.IsNullOrWhiteSpace(overrideLocalPath) ? overrideLocalPath : (config.Deployment.LocalPath ?? "");
        string ftpUrl = !string.IsNullOrWhiteSpace(overrideFtpUrl) ? overrideFtpUrl : (config.Deployment.FtpUrl ?? "");

        string finalDeploymentPath = "";
        string finalFtpUrl = "";

        if (!string.IsNullOrEmpty(overrideLocalPath)) // Si el usuario escribió algo explícito en la cajita local
        {
            finalDeploymentPath = overrideLocalPath;
            config.Deployment.Type = "Local";
        }
        else if (!string.IsNullOrEmpty(overrideFtpUrl)) // O explícito en FTP
        {
            finalFtpUrl = overrideFtpUrl;
            config.Deployment.Type = "FTP";
        }
        else
        {
            if (!string.IsNullOrEmpty(localPath)) { finalDeploymentPath = localPath; config.Deployment.Type = "Local"; }
            if (!string.IsNullOrEmpty(ftpUrl)) { finalFtpUrl = ftpUrl; config.Deployment.Type = "FTP"; }
        }

        if (string.IsNullOrEmpty(finalDeploymentPath) && string.IsNullOrEmpty(finalFtpUrl))
        {
            Log("❌ No se ha configurado un destino válido.");
            return Result.Fail("No se ha configurado un destino de despliegue (Carpeta Local o URL FTP).");
        }

        Log($"📂 Configuración Destino: {(string.IsNullOrEmpty(finalDeploymentPath) ? finalFtpUrl : finalDeploymentPath)}");

        // GUARDAR CONFIGURACIÓN PERMANENTE
        config.Deployment.IsEnabled = true;
        config.Deployment.AppName = appName;
        config.Deployment.PackageId = packageId;
        config.Deployment.Author = author;
        config.Deployment.LocalPath = finalDeploymentPath;
        config.Deployment.FtpUrl = finalFtpUrl;
        config.Deployment.IconPath = iconPath;
        config.Deployment.SplashPath = splashPath;

        Chapi.Infrastructure.Persistence.Settings.ProjectConfigurations.SaveConfig(projectPath, config);
        Log("💾 Configuración Guardada.");

        //2.5. Auto-commit de configuración si cambió (Portabilidad automática) 
        try
        {
            var changes = await _gitRepository.GetChangesAsync(projectPath);
            var configChange = changes.FirstOrDefault(c => c.FilePath.EndsWith("chapi.config.json", StringComparison.OrdinalIgnoreCase));

            if (configChange != null)
            {
                Log("📝 Registrando cambios de configuración en Git...");
                await _gitRepository.CommitAsync(projectPath, "docs(deploy): actualizar configuración de despliegue Chapi", new[] { configChange.FilePath });
            }
        }
        catch (Exception ex)
        {
            Log($"⚠️ Advertencia: No se pudo auto-commitear la configuración ({ex.Message}).");
        }

        // --- INICIO DEL PROCESO DE BUILD ---
        string version = tagName.TrimStart('v', 'V');
        string defaultProjectId = Path.GetFileNameWithoutExtension(projectPath);

        // --- 3.5. Encontrar Raíz de la Solución (.sln / .slnx) ---
        string solutionRoot = projectPath;
        try
        {
            var dir = new DirectoryInfo(projectPath);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Any() || dir.GetFiles("*.slnx").Any())
                {
                    solutionRoot = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }
        }
        catch { }

        string safePackId = System.Text.RegularExpressions.Regex.Replace(packageId, @"[^a-zA-Z0-9_\-\.]", "");
        string baseCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chapi", "BuildCache", safePackId);
        string publishDir = Path.Combine(baseCachePath, "publish");
        string releaseDir = Path.Combine(baseCachePath, "releases");

        Log($"📂 Directorio de Build: {publishDir}");
        Log($"📂 Directorio de Releases: {releaseDir}");

        // 4. Ejecutar dotnet publish
        if (Directory.Exists(publishDir)) Directory.Delete(publishDir, true);

        var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
               .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("Test"))
               .ToArray();

        if (csprojFiles.Length == 0) return Result.Fail("No se encontraron archivos .csproj válidos.");
        string mainCsproj = FindMainCsproj(solutionRoot, csprojFiles, Log);
        defaultProjectId = Path.GetFileNameWithoutExtension(mainCsproj);

        Log($"🔨 Ejecutando compilación sobre {Path.GetFileName(mainCsproj)}...");

        // Verificar si ALGÚN proyecto de la solución tiene referencias COM (no solo el principal)
        bool useMsBuild = csprojFiles.Any(f => HasComReferences(f));
        (bool Success, string Output) buildResult;

        if (useMsBuild)
        {
            var msBuildExe = GetMsBuildPath();
            if (msBuildExe == null)
            {
                Log("❌ Error: Se detectaron referencias COM pero no se encontró MSBuild.exe.");
                return Result.Fail("Referencias COM detectadas. Se requiere Visual Studio instalado para compilar.");
            }

            Log($"⚠️ Referencias COM detectadas. Usando MSBuild: {msBuildExe}");

            // Argumentos MSBuild para Publish
            var msBuildArgs = $"\"{mainCsproj}\" /t:Publish /p:Configuration=Release /p:Platform=x64 /p:PublishDir=\"{publishDir}\" /p:Version={version} /p:Authors=\"{author}\" /p:Product=\"{appName}\"";

            buildResult = await RunCommandAsync(msBuildExe, msBuildArgs, projectPath, onLog);
        }
        else
        {
            var publishArgs = $"publish \"{mainCsproj}\" -c Release -r win-x64 --self-contained -o \"{publishDir}\" -p:Version={version} -p:Authors=\"{author}\" -p:Product=\"{appName}\"";
            buildResult = await RunCommandAsync("dotnet", publishArgs, projectPath, onLog);
        }

        if (!buildResult.Success)
        {
            Log($"❌ Error: Falló la compilación ({(useMsBuild ? "MSBuild" : "dotnet")}).");
            return Result.Fail($"Error en compilación. Revisa el log para detalles.");
        }

        // --- 4.5. Sincronizar Historial Remoto para evitar "Huérfanos de Historial" ---
        Log("🔍 Sincronizando historial de releases remoto...");
        if (!Directory.Exists(releaseDir)) Directory.CreateDirectory(releaseDir);

        try
        {
            if (!string.IsNullOrEmpty(finalDeploymentPath))
            {
                string remoteReleasesPath = Path.Combine(finalDeploymentPath, "RELEASES");
                if (File.Exists(remoteReleasesPath))
                {
                    Log("   > Importando manifest RELEASES desde red...");
                    File.Copy(remoteReleasesPath, Path.Combine(releaseDir, "RELEASES"), true);
                }
            }
            else if (!string.IsNullOrEmpty(finalFtpUrl))
            {
                Log("   > Consultando manifest RELEASES en FTP...");
                var ftpUri = new Uri(finalFtpUrl);
                using var client_sync = new AsyncFtpClient(ftpUri.Host, overrideFtpUser, overrideFtpPass);
                client_sync.Config.ValidateAnyCertificate = true;
                await client_sync.Connect();

                string remotePath = ftpUri.AbsolutePath.TrimEnd('/') + "/RELEASES";
                if (await client_sync.FileExists(remotePath))
                {
                    Log("   > Descargando historial RELEASES activo...");
                    await client_sync.DownloadFile(Path.Combine(releaseDir, "RELEASES"), remotePath, FtpLocalExists.Overwrite);
                }
                await client_sync.Disconnect();
            }
        }
        catch (Exception ex)
        {
            Log($"⚠️ Advertencia: No se pudo sincronizar el historial previo ({ex.Message}).");
        }

        // 5. Ejecutar vpk pack
        Log($"📦 Ejecutando Velopack (vpk) sobre : {releaseDir}...");

        if (safePackId != packageId) Log($"ℹ️ ID Ajustado para Velopack: '{packageId}' -> '{safePackId}'");

        var vpkArgs = $"pack --packId \"{safePackId}\" --packTitle \"{appName}\" --packVersion \"{version}\" --packAuthors \"{author}\" --packDir \"{publishDir}\" --mainExe \"{defaultProjectId}.exe\" --outputDir \"{releaseDir}\"";

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            vpkArgs += $" --icon \"{iconPath}\"";

        if (!string.IsNullOrEmpty(splashPath) && File.Exists(splashPath))
            vpkArgs += $" --splashImage \"{splashPath}\"";

        var vpkEnv = new Dictionary<string, string>
        {
            { "DOTNET_ROLL_FORWARD", "Major" }
        };
        var vpkResult = await RunCommandAsync("vpk", vpkArgs, projectPath, onLog, vpkEnv);

        if (!vpkResult.Success)
        {
            Log("❌ Error: Falló vpk pack.");
            return Result.Fail($"Error en vpk pack. Revisa el log para detalles.");
        }

        Log("✅ Build y Empaquetado exitoso.");

        // 6. Desplegar (Copiar Local/FTP)
        if (!Directory.Exists(releaseDir))
        {
            return Result.Fail("No se encontró la carpeta 'Releases' después del empaquetado.");
        }

        // 6.1 Despliegue Local (Carpeta Compartida)
        if (!string.IsNullOrEmpty(finalDeploymentPath))
        {
            Log($"� Copiando archivos a destino local: {finalDeploymentPath}...");
            try
            {
                if (!Directory.Exists(finalDeploymentPath))
                    Directory.CreateDirectory(finalDeploymentPath);

                foreach (var file in Directory.GetFiles(releaseDir))
                {
                    var fileName = Path.GetFileName(file);
                    Log($"   > Copiando {fileName}...");
                    var destFile = Path.Combine(finalDeploymentPath, fileName);
                    File.Copy(file, destFile, overwrite: true);
                }
                Log($"✅ Despliegue local completado exitosamente.");
            }
            catch (Exception ex)
            {
                Log($"❌ Error copiando archivos: {ex.Message}");
                return Result.Fail($"Error copiando a destino local: {ex.Message}");
            }
        }

        // 6.2 Despliegue FTP
        if (!string.IsNullOrEmpty(finalFtpUrl))
        {
            if (string.IsNullOrWhiteSpace(overrideFtpUser) || string.IsNullOrWhiteSpace(overrideFtpPass))
            {
                Log("❌ Error: Se seleccionó FTP pero no se proporcionaron credenciales.");
                return Result.Fail("Para el despliegue FTP se requiere Usuario y Contraseña.");
            }

            Log($"☁️ Conectando a FTP con FluentFTP: {finalFtpUrl}...");
            try
            {
                var ftpUri = new Uri(finalFtpUrl);
                string host = ftpUri.Host;
                string remotePath = ftpUri.AbsolutePath;

                using var client = new AsyncFtpClient(host, overrideFtpUser, overrideFtpPass);
                client.Config.ValidateAnyCertificate = true; // Ignorar errores SSL
                client.Config.ConnectTimeout = 10000;

                await client.Connect();

                Log($"   > Verificando directorio: {remotePath}");
                if (!await client.DirectoryExists(remotePath))
                {
                    Log($"   > Creando directorio remoto...");
                    await client.CreateDirectory(remotePath);
                }

                var files = Directory.GetFiles(releaseDir);
                if (files.Length == 0) Log("⚠️ Carpeta Releases vacía.");

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string remoteFilePath = remotePath.TrimEnd('/') + "/" + fileName;

                    Log($"   > Subiendo {fileName}...");
                    var status = await client.UploadFile(file, remoteFilePath, FtpRemoteExists.Overwrite, true);

                    if (status == FtpStatus.Failed) throw new Exception($"Falló la subida de {fileName}.");
                }

                await client.Disconnect();
                Log("✅ Despliegue FTP completado exitosamente.");
            }
            catch (Exception ex)
            {
                Log($"❌ Error crítico en FTP: {ex.Message}");
                return Result.Fail($"Falló el despliegue FTP: {ex.Message}");
            }
        }

        return Result.Success();
    }

    private async Task<(bool Success, string Output)> RunCommandAsync(string command, string args, string workingDir, Action<string>? onLog = null, Dictionary<string, string>? envVars = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (envVars != null)
        {
            foreach (var kvp in envVars)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();

        // Redirigir output a log en tiempo real
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onLog?.Invoke(e.Data); // LOG EN VIVO
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onLog?.Invoke($"ERR> {e.Data}"); // LOG ERROR EN VIVO
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, outputBuilder.ToString());
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"EXCEPTION: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private string FindMainCsproj(string solutionRoot, string[] csprojFiles, Action<string> log)
    {
        // Estrategia 1: parsear .sln o .slnx y cruzar con OutputType=WinExe/Exe
        try
        {
            var slnFile = Directory.GetFiles(solutionRoot, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            var slnxFile = Directory.GetFiles(solutionRoot, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();

            IEnumerable<string>? candidatesFromSolution = null;

            if (slnFile != null)
                candidatesFromSolution = ParseSlnProjects(slnFile, solutionRoot);
            else if (slnxFile != null)
                candidatesFromSolution = ParseSlnxProjects(slnxFile, solutionRoot);

            if (candidatesFromSolution != null)
            {
                var solutionCsprojs = candidatesFromSolution
                    .Where(p => csprojFiles.Any(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var exeProject = solutionCsprojs.FirstOrDefault(p => IsExecutableProject(p));
                if (exeProject != null)
                {
                    log($"✅ Proyecto principal detectado desde solución: {Path.GetFileName(exeProject)}");
                    return exeProject;
                }
            }
        }
        catch (Exception ex)
        {
            log($"⚠️ No se pudo parsear la solución: {ex.Message}");
        }

        // Estrategia 2: buscar OutputType=WinExe o Exe entre todos los .csproj encontrados
        var exeByOutputType = csprojFiles.FirstOrDefault(p => IsExecutableProject(p));
        if (exeByOutputType != null)
        {
            log($"✅ Proyecto principal detectado por OutputType: {Path.GetFileName(exeByOutputType)}");
            return exeByOutputType;
        }

        // Estrategia 3: fallback - ruta más corta (comportamiento anterior)
        var fallback = csprojFiles.OrderBy(f => f.Length).First();
        log($"⚠️ No se detectó proyecto principal exacto, usando fallback: {Path.GetFileName(fallback)}");
        return fallback;
    }

    private IEnumerable<string> ParseSlnProjects(string slnPath, string solutionRoot)
    {
        var lines = File.ReadAllLines(slnPath);
        var regex = new System.Text.RegularExpressions.Regex(
            @"Project\(""[^""]+""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                var relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(solutionRoot, relativePath));
                if (File.Exists(fullPath))
                    yield return fullPath;
            }
        }
    }

    private IEnumerable<string> ParseSlnxProjects(string slnxPath, string solutionRoot)
    {
        var doc = System.Xml.Linq.XDocument.Load(slnxPath);
        foreach (var projectElement in doc.Descendants("Project"))
        {
            var path = projectElement.Attribute("Path")?.Value;
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(Path.Combine(solutionRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(fullPath))
                    yield return fullPath;
            }
        }
    }

    private bool IsExecutableProject(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);
            return System.Text.RegularExpressions.Regex.IsMatch(
                content,
                @"<OutputType>\s*(WinExe|Exe)\s*</OutputType>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }

    private string? _msBuildPath;

    private string? GetMsBuildPath()
    {
        if (_msBuildPath != null) return _msBuildPath;

        // 1. Intentar con vswhere (Método Oficial para detectar cualquier versión, incluida Insider/Preview)
        try
        {
            var vswherePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (File.Exists(vswherePath))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = vswherePath,
                    Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim().Split('\r', '\n').FirstOrDefault();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        _msBuildPath = output;
                        return _msBuildPath;
                    }
                }
            }
        }
        catch { /* Ignorar errores y probar rutas fijas */ }

        // 2. Fallback: Rutas comunes (incluyendo Preview/Insider y VS 18 Insiders)
        var paths = new[] {
            @"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe", // Nueva ruta detectada para VS 2026 Insider
            @"C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
             // Rutas VS 2019
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
        };

        _msBuildPath = paths.FirstOrDefault(File.Exists);
        return _msBuildPath;
    }

    private bool HasComReferences(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);
            return content.Contains("<COMReference");
        }
        catch { return false; }
    }

    private async Task<bool> EnsureVelopackInstalled(Action<string> log)
    {
        // 1. Verificar si 'vpk' existe ejecutando --version
        try
        {
            var checkResult = await RunCommandAsync("vpk", "--version", Environment.CurrentDirectory);
            if (checkResult.Success)
            {
                // log("✅ Herramienta Velopack (vpk) detectada.");
                return true;
            }
        }
        catch { /* Falló la ejecución, no existe */ }

        log("⚠️ Herramienta Velopack (vpk) no encontrada. Intentando instalar automáticamente...");

        // 2. Intentar instalar globalmente
        var installResult = await RunCommandAsync("dotnet", "tool install -g vpk", Environment.CurrentDirectory, log);

        if (installResult.Success)
        {
            log("✅ Velopack instalado correctamente.");
            return true;
        }
        else
        {
            // 3. Verificar si el error es porque ya está instalado pero no en el PATH
            if (installResult.Output.Contains("already installed") || installResult.Output.Contains("ya está instalada"))
            {
                log("⚠️ Velopack ya está instalado pero no se encuentra en el PATH. Intentando usar ruta directa...");
                // Intentar agregar ruta de herramientas globales al PATH de la sesión actual no funciona bien desde aquí.
                // Retornamos true para intentar seguir, pero quizás falle luego si no usamos ruta absoluta.
                // Mejor: Devolver true y dejar que falle después si no lo encuentra.
                return true;
            }

            log("❌ Error fatal al instalar Velopack.");
            log("💡 SUGERENCIA: Ejecuta 'dotnet tool install -g vpk' manualmente en una terminal con acceso a internet.");
            return false;
        }
    }
}
