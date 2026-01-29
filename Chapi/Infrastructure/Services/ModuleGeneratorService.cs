using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Chapi.Helper.Roslyn;
using Chapi.Helper.Entities;
using Chapi.Helper.GitHelper;
using Chapi.Services;

namespace Chapi.Infrastructure.Services;

public interface IModuleGeneratorService
{
    Task GenerateModuleAsync(string projectDirectory, string moduleName, string dbName);
}

public class ModuleGeneratorService : IModuleGeneratorService
{
    private readonly Action<string> _logger;

    public ModuleGeneratorService(Action<string> logger)
    {
        _logger = logger;
    }

    public async Task GenerateModuleAsync(string projectDirectory, string moduleName, string dbName)
    {
        moduleName = char.ToUpper(moduleName[0]) + moduleName[1..];
        _logger?.Invoke($"Generando módulo: {moduleName}");

        string apiProjectPath = FindApiDirectory.GetDirectory(projectDirectory);
        if (apiProjectPath == null)
            throw new Exception("No se pudo detectar el proyecto API.");

        string apiPath = Path.Combine(projectDirectory, Path.GetFileName(apiProjectPath), "Controllers", moduleName);
        string appPath = Path.Combine(projectDirectory, "Application", moduleName);
        string domainPath = Path.Combine(projectDirectory, "Domain", moduleName);
        string infraPath = Path.Combine(projectDirectory, "Infrastructure", dbName, "Repositories", moduleName);

        Directory.CreateDirectory(apiPath);
        Directory.CreateDirectory(appPath);
        Directory.CreateDirectory(domainPath);
        Directory.CreateDirectory(infraPath);

        var defaultOperations = new[] { "Get", "Post", "GetById" };

        foreach (var operation in defaultOperations)
        {
            var rollbackEntry = RollbackManager.StartTransaction(moduleName, moduleName, operation);
            try
            {
                AddApiControllerMethod.Add(apiPath, moduleName, operation, moduleName, rollbackEntry);
                AddApplicationMethod.Add(appPath, moduleName, operation, moduleName, rollbackEntry);
                await AddDomainMethod.Add(domainPath, moduleName, operation, moduleName, rollbackEntry);
                await AddInfrastructureMethod.Add(infraPath, moduleName, dbName, operation, moduleName, rollbackEntry);

                string dependencyInjectionPath = Path.Combine(projectDirectory, Path.GetFileName(apiProjectPath), "Config", "DependencyInjection.cs");
                var diContent = File.ReadAllText(dependencyInjectionPath);
                RollbackManager.RecordFileModification(rollbackEntry, dependencyInjectionPath, diContent);
                AddDependencyInjection.Add(dependencyInjectionPath, moduleName, new[] { operation });

                RollbackManager.CommitTransaction(rollbackEntry);
            }
            catch (Exception ex)
            {
                var tempPath = RollbackManager.GetRollbackFilePathForEntry(rollbackEntry);
                RollbackManager.CommitTransaction(rollbackEntry);
                RollbackManager.ExecuteRollback(tempPath);
                throw new Exception($"Error al generar operación {operation}: {ex.Message}");
            }
        }
    }
}
