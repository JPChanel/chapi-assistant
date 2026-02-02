using Chapi.Domain.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using System.Text;
using static Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager;

using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Infrastructure.Services;
using static Chapi.Infrastructure.Roslyn.GenerationStandards;

namespace Chapi.Infrastructure.Roslyn;

public class AddInfrastructureMethod
{
    public static async Task<RollbackEntry> Add(
        string projectPath,
        string moduleName,
        string dbName,
        string operation,
        string methodName,
        RollbackEntry? rollbackEntry = null,
        SPAnalysisResult? aiResult = null,
        bool isArdalisStyle = false)
    {
        // Sanitizar nombres
        string cleanModule = moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        string lastModuleSegment = cleanModule.Split('.').Last();
        
        // Limpiar methodName si viene con ruta
        var cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        if (cleanMethodName != methodName) methodName = cleanMethodName;

        if (!OperationConfigs.TryGetValue(operation.ToLower(), out var config))
            throw new ArgumentException($"Operación {operation} no soportada.");

        var className = FormatPattern(config.RepositoryClassNamePattern, methodName);

        if (!Directory.Exists(projectPath))
            Directory.CreateDirectory(projectPath);
            
        // Generar DTO (Siempre, ya que la clase repositorio lo referencia)
        var dtoPath = Path.Combine(projectPath, "Dto");
        if (!Directory.Exists(dtoPath))
            Directory.CreateDirectory(dtoPath);

        var dtoFile = Path.Combine(dtoPath, $"{lastModuleSegment}Dto.cs");
        GenerateOrUpdateDto(dtoFile, dbName, cleanModule, lastModuleSegment, aiResult ?? new SPAnalysisResult());

        var filePath = Path.Combine(projectPath, $"{className}.cs");
        bool fileExisted = File.Exists(filePath);
        string? originalContent = fileExisted ? await File.ReadAllTextAsync(filePath) : null;

        // ?? Registrar rollback
        if (rollbackEntry != null)
        {
            if (fileExisted)
                RollbackManager.RecordFileModification(rollbackEntry, filePath, originalContent!);
            else
                RollbackManager.RecordFileCreation(rollbackEntry, filePath);
        }

        if (!fileExisted)
        {
            await GenerateInfrastructureFile(filePath, config, cleanModule, lastModuleSegment, methodName, dbName, operation, aiResult, isArdalisStyle);
            Msg.Assistant($"?? Creado Infrastructure.{cleanModule}.{className}");
        }
        else
        {
            await AddMethodToExistingClass(filePath, config, operation, cleanModule, lastModuleSegment, methodName, aiResult, isArdalisStyle);
        }

        return rollbackEntry!;
    }

    // ?? Agregar método nuevo si la clase ya existe
    private static async Task AddMethodToExistingClass(
        string filePath,
        GenerationStandards.OperationConfig config,
        string operation,
        string moduleName,
        string lastModuleSegment,
        string methodName,
        SPAnalysisResult? aiResult,
        bool isArdalisStyle)
    {
        var code = await File.ReadAllTextAsync(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = await syntaxTree.GetRootAsync();

        var classNode = root.DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault();

        if (classNode == null)
            return;

        string entityName = lastModuleSegment; 

        var methodCode = GenerateMethodCode(config, moduleName, entityName, methodName, aiResult, isArdalisStyle);
        var newMethod = SyntaxFactory.ParseMemberDeclaration(methodCode)!;
        var newClass = classNode.AddMembers(newMethod);
        var newRoot = root.ReplaceNode(classNode, newClass);

        await File.WriteAllTextAsync(filePath, newRoot.NormalizeWhitespace().ToFullString());
        Msg.Assistant($"? Método agregado: {methodName} en {Path.GetFileName(filePath)}");
    }

    // ??? Generar archivo completo de infraestructura
    private static async Task GenerateInfrastructureFile(
        string filePath,
        OperationConfig config,
        string moduleName, 
        string entityName, 
        string methodName, 
        string dbName,
        string operation,
        SPAnalysisResult? aiResult = null,
        bool isArdalisStyle = false)
    {
        var className = Path.GetFileNameWithoutExtension(filePath);

        // Determine Interface and Method implementation strategy
        string interfaceToImplement;
        if (isArdalisStyle)
        {
             interfaceToImplement = FormatPattern(config.GenericRepositoryInterfacePattern, methodName);
        }
        else
        {
            // Legacy Interface Names
             interfaceToImplement = FormatPattern(config.ApplicationInterfaceNamePattern, methodName);
        }

        var sb = new StringBuilder($@"
            using Dapper;
            using Domain.Shared.Interface.Base; 
            using Domain.{moduleName}.Entities;
            using Domain.Shared.Entities.Responses;
            {(!isArdalisStyle ? $"using Domain.{moduleName}.Interfaces;" : "")}
            using {dbName}.Connections;
            using {dbName}.Repositories.Shared.Parser;
            using {dbName}.Repositories.{moduleName}.Dto;

            namespace {dbName}.Repositories.{moduleName};

            public class {className}({dbName}Connection connection) : {dbName}Repository(connection), {interfaceToImplement}
            {{
            ");

        sb.AppendLine(GenerateMethodCode(config, moduleName, entityName, methodName, aiResult, isArdalisStyle));
        sb.AppendLine("}");

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private static string GenerateMethodCode(GenerationStandards.OperationConfig config, string moduleName, string entityName, string methodName, SPAnalysisResult? aiResult, bool isArdalisStyle)
    {
        var spName = aiResult?.StoredProcedureName ?? "";
        var hasParams = aiResult?.Parameters?.Any() == true;
        var hasMapper = aiResult?.ResponseMapper?.Any() == true;

        var paramBlock = hasParams
         ? string.Join(",\n                ", aiResult!.Parameters)
         : "";

        var mapperBlock = hasMapper
            ? string.Join("\n                        ", aiResult!.ResponseMapper)
            : "";
        spName = "\"" + spName + "\"";
        
        
        string implMethodName = isArdalisStyle ? config.GenericRepositoryMethodNamePattern : FormatPattern(config.RepositoryMethodNamePattern, methodName);
        string requestType = FormatPattern(config.EndpointRequestClassPattern, methodName);

        if (config.RepositoryNamespaceTag == "Search") // GET
        {
             return $@"
    public async Task<{(isArdalisStyle ? "IEnumerable<object>" : "object")}> {implMethodName}({requestType} request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryAsync<{entityName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return GenericListMapper.ParseCollection(response, dto => new {{
                    {mapperBlock}
        }});
    }}";
        }
        
        if (config.RepositoryNamespaceTag == "Find") // GET BY ID
        {
             return $@"
    public async Task<{(isArdalisStyle ? "object?" : "object")}> {implMethodName}(int code)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock ?? "Code = code"}  
        }};
        var response = await cn.QueryFirstOrDefaultAsync<{entityName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        if (response == null) return null;
        return GenericListMapper.Parse(response, dto => new {{
               {mapperBlock}
        }});
    }}";
        }

        // POST / PUT / DELETE (Response based)
        return $@"
    public async Task<Response> {implMethodName}({(requestType == "int" ? "int code" : $"{requestType} request")})
    {{
        using var cn = Connection();
        var parameters = new {{
                {(requestType == "int" ? (paramBlock ?? "Code = code") : paramBlock)}
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}";
    }


    // ?? Generar clase DTO
    private static void GenerateOrUpdateDto(string dtoPath, string dbName, string moduleNamespace, string entityName, SPAnalysisResult? aiResult)
    {
        var className = $"{entityName}Dto";
        var dtoFields = aiResult.DTOFields ?? new();
        // Si no existe, se crea completo
        if (!File.Exists(dtoPath))
        {
            var content = $@"
using System;
using Chapi.Infrastructure.Services;
namespace {dbName}.Repositories.{moduleNamespace}.Dto;

public class {className}
{{
    {string.Join("\n    ", dtoFields)}
}}";
            File.WriteAllText(dtoPath, content);
            return;
        }

        // Si ya existe, agregamos solo campos nuevos
        var existingCode = File.ReadAllText(dtoPath);
        var syntaxTree = CSharpSyntaxTree.ParseText(existingCode);
        var root = syntaxTree.GetRoot();

        var classNode = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);

        if (classNode == null)
            return;

        var existingProps = classNode.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(p => p.Identifier.Text)
            .ToHashSet();

        var newFields = dtoFields
            .Select(f => SyntaxFactory.ParseMemberDeclaration(f))
            .Where(p => p is PropertyDeclarationSyntax prop && !existingProps.Contains(prop.Identifier.Text))
            .ToList();

        if (newFields.Any())
        {
            var updatedClass = classNode.AddMembers(newFields.ToArray());
            var newRoot = root.ReplaceNode(classNode, updatedClass);
            File.WriteAllText(dtoPath, newRoot.NormalizeWhitespace().ToFullString());
        }
    }
}




