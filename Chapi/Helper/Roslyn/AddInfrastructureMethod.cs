using Chapi.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using System.Text;
using static Chapi.Helper.RollbackManager;

namespace Chapi.Helper.Roslyn;

public class AddInfrastructureMethod
{
    public static async Task<RollbackEntry> Add(
        string projectPath,
        string moduleName,
        string dbName,
        string operation,
        string methodName,
        RollbackEntry? rollbackEntry = null,
        SPAnalysisResult? aiResult = null)
    {
        var className = operation switch
        {
            "Get" => $"Search{methodName}Repository",
            "GetById" => $"Find{methodName}Repository",
            "Post" => $"{methodName}Repository",
            "Put" => $"Update{methodName}Repository",
            "Delete" => $"Delete{methodName}Repository",
            _ => throw new ArgumentException("Método no soportado")
        };

        if (!Directory.Exists(projectPath))
            Directory.CreateDirectory(projectPath);
        // Generar DTO si hay datos IA
        if (aiResult != null)
        {
            var dtoPath = Path.Combine(projectPath, "Dto");
            if (!Directory.Exists(dtoPath))
                Directory.CreateDirectory(dtoPath);

            var dtoFile = Path.Combine(dtoPath, $"{moduleName}Dto.cs");
            GenerateOrUpdateDto(dtoFile, moduleName, aiResult);
        }
        var filePath = Path.Combine(projectPath, $"{className}.cs");
        bool fileExisted = File.Exists(filePath);
        string? originalContent = fileExisted ? await File.ReadAllTextAsync(filePath) : null;

        // 📝 Registrar rollback
        if (rollbackEntry != null)
        {
            if (fileExisted)
                RollbackManager.RecordFileModification(rollbackEntry, filePath, originalContent!);
            else
                RollbackManager.RecordFileCreation(rollbackEntry, filePath);
        }

        if (!fileExisted)
        {
            await GenerateInfrastructureFile(filePath, moduleName, methodName, dbName, operation, aiResult);
            Msg.Assistant($"🧩 Creado Infrastructure.{moduleName}.{className}");
        }
        else
        {
            await AddMethodToExistingClass(filePath, operation, moduleName, methodName, aiResult);
        }

        return rollbackEntry!;
    }

    // 🔧 Agregar método nuevo si la clase ya existe
    private static async Task AddMethodToExistingClass(
        string filePath,
        string operation,
        string moduleName,
        string methodName,
        SPAnalysisResult? aiResult)
    {
        var code = await File.ReadAllTextAsync(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = await syntaxTree.GetRootAsync();

        var classNode = root.DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault();

        if (classNode == null)
            return;

        var methodCode = GenerateMethodCode(operation, moduleName, methodName, aiResult);
        var newMethod = SyntaxFactory.ParseMemberDeclaration(methodCode)!;
        var newClass = classNode.AddMembers(newMethod);
        var newRoot = root.ReplaceNode(classNode, newClass);

        await File.WriteAllTextAsync(filePath, newRoot.NormalizeWhitespace().ToFullString());
        Msg.Assistant($"✅ Método agregado: {methodName} en {Path.GetFileName(filePath)}");
    }

    // 🏗️ Generar archivo completo de infraestructura
    private static async Task GenerateInfrastructureFile(
        string filePath,
        string moduleName,
        string methodName,
        string dbName,
        string operation,
        SPAnalysisResult? aiResult = null)
    {
        var className = operation switch
        {
            "Get" => $"Search{methodName}Repository",
            "GetById" => $"Find{methodName}Repository",
            "Post" => $"{methodName}Repository",
            "Put" => $"Update{methodName}Repository",
            "Delete" => $"Delete{methodName}Repository",
            _ => throw new ArgumentException("Método no soportado")
        };

        var sb = new StringBuilder($@"
            using Dapper;
            using Domain.{moduleName}.Interfaces;
            using Domain.{moduleName}.Entities;
            using {dbName}.Connections;
            using {dbName}.Repositories.Shared.Parser;

            namespace {dbName}.Repositories.{moduleName};

            public class {className}({dbName}Connection connection) : {dbName}Repository(connection), I{className}
            {{
            ");

        sb.AppendLine(GenerateMethodCode(operation, moduleName, methodName, aiResult));
        sb.AppendLine("}");

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private static string GenerateMethodCode(string operation, string moduleName, string methodName, SPAnalysisResult? aiResult)
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
        // 🧩 Genera según tipo de operación
        return operation switch
        {
            // 🔍 GET (Listar o Buscar)
            "Get" => $@"
    public async Task<object> Search{methodName}(Search{methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryAsync<{moduleName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return GenericListMapper.ParseCollection(response, dto => new {{
                    {mapperBlock}
        }});
    }}",

            // 🔎 GET BY ID (Buscar un registro específico)
            "GetById" => $@"
    public async Task<object> Find{methodName}(int code)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock ?? "Code = code"}  
        }};
        var response = await cn.QueryFirstOrDefaultAsync<{moduleName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        if (response == null) return null;
        return GenericListMapper.Parse(response, dto => new {{
               {mapperBlock}
        }});
    }}",

            // 💾 POST (Crear registro)
            "Post" => $@"
    public async Task<Response> {methodName}({methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}",

            // 🛠️ PUT (Actualizar registro)
            "Put" => $@"
    public async Task<Response> Update{methodName}(Update{methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}",

            // ❌ DELETE (Eliminar registro)
            "Delete" => $@"
    public async Task<Response> Delete{methodName}(int code)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock ?? "Code = code"}  
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}",

            _ => throw new ArgumentException($"Operación '{operation}' no soportada en GenerateMethodCode()")
        };
    }

    // 🧱 Generar clase DTO
    private static void GenerateOrUpdateDto(string dtoPath, string moduleName, SPAnalysisResult? aiResult)
    {
        var className = $"{moduleName}Dto";
        var dtoFields = aiResult.DTOFields ?? new();
        // Si no existe, se crea completo
        if (!File.Exists(dtoPath))
        {
            var content = $@"
using System;

namespace Infrastructure.Repositories.{moduleName}.Dto;

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
