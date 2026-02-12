using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Infrastructure.Services;
using System.IO;
using static Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager;
using static Chapi.Infrastructure.Roslyn.GenerationStandards;

namespace Chapi.Infrastructure.Roslyn;

public static class AddApiEndpointMethod
{
    public static RollbackEntry Add(string apiPath, string moduleName, string operation, string methodName, RollbackEntry rollbackEntry = null, bool includeAppLayer = false)
    {
        if (!OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"Operacion no soportada: {operation}");
            return rollbackEntry;
        }

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
                fileNameBase = operation.ToLower() switch
                {
                    "get" => $"Search{cleanMethodName}",
                    "getbyid" => $"Get{cleanMethodName}ById",
                    "post" => cleanMethodName,
                    "put" => $"Update{cleanMethodName}",
                    "delete" => $"Delete{cleanMethodName}",
                    _ => $"{config.EndpointFileName}{cleanMethodName}"
                };
            }
        }

        if (operation.ToLower() == "post" && fileNameBase == "{0}")
        {
            fileNameBase = cleanMethodName.Equals(lastModuleName, StringComparison.OrdinalIgnoreCase) ? "Execute" : cleanMethodName;
        }

        var fileName = fileNameBase + ".cs";
        var filePath = Path.Combine(apiPath, fileName);

        Directory.CreateDirectory(apiPath);

        if (File.Exists(filePath))
        {
            Msg.Assistant($"El endpoint {fileName} ya existe.");
            return rollbackEntry;
        }

        var code = GenerateEndpointClass(moduleName, fileNameBase, operation, methodName, config, includeAppLayer);

        File.WriteAllText(filePath, code);

        if (rollbackEntry != null)
        {
            RollbackManager.RecordFileCreation(rollbackEntry, filePath);
        }

        Msg.Assistant($"Endpoint creado: {fileName}");
        return rollbackEntry;
    }

    private static string GenerateEndpointClass(string moduleName, string className, string operation, string methodName, OperationConfig config, bool includeAppLayer)
    {
        string cleanModule = moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        string cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();

        string route = $"/{cleanModule.ToLower().Replace('.', '/')}";
        if (operation.ToLower() == "getbyid") route += "/{id}";

        string requestType = FormatPattern(config.EndpointRequestClassPattern, cleanMethodName);
        string httpVerb = config.HttpAttributeName;

        string decimalsInterface;
        string variableName;
        string callMethod;
        string applicationUsing = $"using Application.{cleanModule};";

        if (includeAppLayer)
        {
            string baseServiceName = FormatPattern(config.ApplicationClassNamePattern, cleanMethodName);
            decimalsInterface = baseServiceName + "Service";
            variableName = "service";
            callMethod = FormatPattern(config.ApplicationMethodNamePattern, cleanMethodName);
        }
        else
        {
            decimalsInterface = FormatPattern(config.GenericRepositoryInterfacePattern, cleanMethodName);
            variableName = "repository";
            callMethod = config.GenericRepositoryMethodNamePattern;
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
        if (op == "getbyid") return $"[FromRoute] {requestType} req, CancellationToken ct = default";
        if (op == "post") return $"[FromBody] {requestType} req, CancellationToken ct = default";
        return $"{requestType} req, CancellationToken ct = default";
    }

    private static string GetServiceCallParams(string operation)
    {
        return "req";
    }
}
