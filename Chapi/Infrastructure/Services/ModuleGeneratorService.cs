using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Chapi.Infrastructure.Roslyn;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Git;
using Chapi.Infrastructure.Services;

using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Infrastructure.Common;
using Chapi.Domain.Interfaces;
namespace Chapi.Infrastructure.Services;

public interface IModuleGeneratorService
{
    Task GenerateModuleAsync(string projectDirectory, string moduleName, string dbName);
}

public class ModuleGeneratorService : IModuleGeneratorService
{
    private readonly INotificationService _notificationService;

    public ModuleGeneratorService(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task GenerateModuleAsync(string projectDirectory, string moduleName, string dbName)
    {
        moduleName = char.ToUpper(moduleName[0]) + moduleName[1..];
        _notificationService.ShowInfo($"Generando modulo: {moduleName}");

        string apiProjectPath = FindApiDirectory.GetDirectory(projectDirectory);
        if (apiProjectPath == null)
            throw new Exception("No se pudo detectar el proyecto API.");

        string apiFolderName = Path.GetFileName(apiProjectPath);
        
        // Determinar si es Ardalis o Classic
        bool isArdalis = Directory.Exists(Path.Combine(apiProjectPath, "Endpoints"));
        string apiSubFolder = isArdalis ? "Endpoints" : "Controllers";
        
        _notificationService.ShowInfo($"Arquitectonico detectado: {(isArdalis ? "Ardalis (Endpoints)" : "Classic (Controllers)")}");

        string apiPath = Path.Combine(apiProjectPath, apiSubFolder, moduleName);
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
                if (isArdalis)
                {
                    AddApiEndpointMethod.Add(apiPath, moduleName, operation, moduleName, rollbackEntry, includeAppLayer: true);
                }
                else
                {
                    AddApiControllerMethod.Add(apiPath, moduleName, operation, moduleName, rollbackEntry);
                }

                AddApplicationMethod.Add(appPath, moduleName, operation, moduleName, rollbackEntry, useGenericRepository: isArdalis);
                await AddDomainMethod.Add(domainPath, moduleName, operation, moduleName, rollbackEntry, aiResult: null, isArdalisStyle: isArdalis);
                await AddInfrastructureMethod.Add(infraPath, moduleName, dbName, operation, moduleName, rollbackEntry, aiResult: null, isArdalisStyle: isArdalis);

                string dependencyInjectionPath = Path.Combine(apiProjectPath, "Config", "DependencyInjection.cs");
                if (File.Exists(dependencyInjectionPath))
                {
                    var diContent = File.ReadAllText(dependencyInjectionPath);
                    RollbackManager.RecordFileModification(rollbackEntry, dependencyInjectionPath, diContent);
                    
                    // En Ardalis usualmente se usa Scrutor, pero si existe el archivo intentamos registrar
                    // aunque el estandar dice que Scrutor deberia hacerlo. Solo lo hacemos para Classic por ahora
                    // para no romper el estandar de Ardalis de "auto-discovery".
                    if (!isArdalis)
                    {
                        AddDependencyInjection.Add(dependencyInjectionPath, moduleName, new[] { operation });
                    }
                }

                RollbackManager.CommitTransaction(rollbackEntry);
            }
            catch (Exception ex)
            {
                var tempPath = RollbackManager.GetRollbackFilePathForEntry(rollbackEntry);
                RollbackManager.CommitTransaction(rollbackEntry);
                // TODO: Fix ExecuteRollback call - needs RollbackEntry, not string
                // 
                throw new Exception($"Error al generar operacion {operation}: {ex.Message}");
            }
        }
    }
}







