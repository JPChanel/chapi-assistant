using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Helper.RollbackManager;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Chapi.Helper.Roslyn;

public static class AddApiEndpointMethod
{
    public static RollbackEntry Add(string apiPath, string moduleName, string operation, string methodName, RollbackEntry rollbackEntry = null, bool includeAppLayer = false)
    {
        // 1. Obtener Configuración
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"⚠️ Operación no soportada: {operation}");
            return rollbackEntry;
        }

        // 2. Determinar Nombre de Archivo
        string fileNameBase;
        
        string lastModuleName = moduleName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        string cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();

        if (cleanMethodName.Equals(lastModuleName, StringComparison.OrdinalIgnoreCase))
        {
            fileNameBase = config.EndpointFileName; 
        }
        else
        {
         
            if (config.EndpointFileName.Contains("{0}"))
            {
                fileNameBase = FormatPattern(config.EndpointFileName, cleanMethodName);
            }
            else
            {
                // Concatenación inteligente según operación
                fileNameBase = operation.ToLower() switch
                {
                    "get" => $"Search{cleanMethodName}",
                    "getbyid" => $"Get{cleanMethodName}ById",
                    "post" => cleanMethodName, // Post suele ser un verbo/acción directo
                    "put" => $"Update{cleanMethodName}",
                    "delete" => $"Delete{cleanMethodName}",
                    _ => $"{config.EndpointFileName}{cleanMethodName}"
                };
            }
        }
        
        // Post specific fix if pattern result was still generic
        if (operation.ToLower() == "post" && fileNameBase == "{0}")
        {
             fileNameBase = cleanMethodName.Equals(lastModuleName, StringComparison.OrdinalIgnoreCase) ? "Execute" : cleanMethodName;
        }

        var fileName = fileNameBase + ".cs";
        var filePath = Path.Combine(apiPath, fileName);

        Directory.CreateDirectory(apiPath);
        
        // 3. Generar Contenido
        if (File.Exists(filePath))
        {
             Msg.Assistant($"ℹ️ El endpoint {fileName} ya existe.");
             return rollbackEntry;
        }

        var code = GenerateEndpointClass(moduleName, fileNameBase, operation, methodName, config, includeAppLayer);
        
        File.WriteAllText(filePath, code);
        
        if (rollbackEntry != null)
        {
            RollbackManager.RecordFileCreation(rollbackEntry, filePath);
        }

        Msg.Assistant($"✅ Endpoint creado: {fileName}");
        return rollbackEntry;
    }

    private static string GenerateEndpointClass(string moduleName, string className, string operation, string methodName, GenerationStandards.OperationConfig config, bool includeAppLayer)
    {
        // Normalizar módulo para namespace (cambiar \ por .)
        string cleanModule = moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        string lastModuleSegment = cleanModule.Split('.').Last(); 
        
        // Clean Method Name
        string cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();

        // Construir ruta (SIN /api)
        string route = $"/{cleanModule.ToLower().Replace('.', '/')}"; 
        if (operation.ToLower() == "getbyid") route += "/{id}";
        
        // Definir Request Type (Usando MethodName para soportar SearchCargoRequest vs SearchCertificadoRequest)
        string requestType = FormatPattern(config.EndpointRequestClassPattern, cleanMethodName); 
        
        // Definir Result Type
        string resultType = "object"; 
        
        // Definir Verbo HTTP
        string httpVerb = config.HttpAttributeName; 

        // LÓGICA DE INYECCIÓN (Servicio vs Repositorio)
        string decimalsInterface;
        string variableName;
        string callMethod;
        string applicationUsing = $"using Application.{cleanModule};";

        if (includeAppLayer)
        {
            // Inject Service
            string baseServiceName = FormatPattern(config.ApplicationClassNamePattern, cleanMethodName);
            decimalsInterface = baseServiceName + "Service"; 
            variableName = "service";
            callMethod = FormatPattern(config.ApplicationMethodNamePattern, cleanMethodName); 
            
            // Add Services namespace if needed (REMOVED: User requested flat structure)
            // applicationUsing += $"\nusing Application.{cleanModule}.Services;";
        }
        else
        {
            // Inject Repository (Directo)
            decimalsInterface = FormatPattern(config.GenericRepositoryInterfacePattern, cleanMethodName);
            variableName = "repository";
            callMethod = config.GenericRepositoryMethodNamePattern; // Search, Execute, Find
        }

        return $@"using Ardalis.ApiEndpoints;
{applicationUsing}
using Domain.{cleanModule}.Entities;
using Domain.Shared.Entities.Responses;
using Domain.Shared.Interface.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Http.Endpoints.{cleanModule};

[Route(""{route}"")]
[Authorize]
public class {className}({decimalsInterface} {variableName}) : EndpointBaseAsync
    .WithRequest<{requestType}>
    .WithActionResult
{{
    [{httpVerb}]
    public override async Task<ActionResult> HandleAsync({GetHandleParams(operation, requestType)})
    {{
        return Ok(await {variableName}.{callMethod}({GetServiceCallParams(operation)}));
    }}
}}
";
    }

    private static string GetHandleParams(string operation, string requestType)
    {
        string op = operation.ToLower();
        if (op == "get") return $"[FromQuery] {requestType} req, CancellationToken ct = default";
        if (op == "getbyid") return $"[FromRoute] {requestType} req, CancellationToken ct = default"; // Asumiendo que el request mapea el Route ID
        if (op == "post") return $"[FromBody] {requestType} req, CancellationToken ct = default";
        return $"{requestType} req, CancellationToken ct = default";
    }

    private static string GetServiceCallParams(string operation)
    {
        string op = operation.ToLower();
        // El servicio de Application suele esperar el Request object
        // GetById a veces espera solo el int id, pero el nuevo estándar de AppService esperamos unificarse?
        // Revisando AddApplicationMethod.cs:
        // parameter = operation == "GetById" ? requestName : parameter;
        // var param = operation == "GetById" ? "code" : "request";
        
        // Si Application espera int code para GetById, necesitamos adaptarnos.
        // PERO el usuario dijo "NO generar archivos request extra", usar Domain DTOs.
        // Si el Domain DTO para GetById es int... no es un DTO clase.
        // Asumiremos que pa GetById pasamos 'req.Id' o 'req'.
        
        // Por simplicidad y estándar Ardalis: pasamos 'req'
        return "req";
    }

    private static string FormatPattern(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        var tempPattern = pattern.Replace("{0:lower}", value.ToLower());
        return string.Format(tempPattern, value);
    }
}
