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
        SPAnalysisResult? aiResult = null,
        bool useGenericInterface = false)
    {
        // Sanitizar nombres
        string cleanModule = moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        string lastModuleSegment = cleanModule.Split('.').Last();
        
        // Limpiar methodName si viene con ruta
        var cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        if (cleanMethodName != methodName) methodName = cleanMethodName;

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
            
        // Generar DTO (Siempre, ya que la clase repositorio lo referencia)
        var dtoPath = Path.Combine(projectPath, "Dto");
        if (!Directory.Exists(dtoPath))
            Directory.CreateDirectory(dtoPath);

        var dtoFile = Path.Combine(dtoPath, $"{lastModuleSegment}Dto.cs");
        GenerateOrUpdateDto(dtoFile, dbName, cleanModule, lastModuleSegment, aiResult ?? new SPAnalysisResult());

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
            await GenerateInfrastructureFile(filePath, cleanModule, lastModuleSegment, methodName, dbName, operation, aiResult, useGenericInterface);
            Msg.Assistant($"🧩 Creado Infrastructure.{cleanModule}.{className}");
        }
        else
        {
            await AddMethodToExistingClass(filePath, operation, cleanModule, methodName, aiResult, useGenericInterface);
        }

        return rollbackEntry!;
    }

    // 🔧 Agregar método nuevo si la clase ya existe
    private static async Task AddMethodToExistingClass(
        string filePath,
        string operation,
        string moduleName,
        string methodName,
        SPAnalysisResult? aiResult,
        bool useGenericInterface)
    {
        var code = await File.ReadAllTextAsync(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = await syntaxTree.GetRootAsync();

        var classNode = root.DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault();

        if (classNode == null)
            return;

        string entityName = moduleName.Split('.').Last(); // Logic for entity naming

        var methodCode = GenerateMethodCode(operation, moduleName, entityName, methodName, aiResult, useGenericInterface);
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
        string entityName, 
        string methodName, 
        string dbName,
        string operation,
        SPAnalysisResult? aiResult = null,
        bool useGenericInterface = false)
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

        // Determine Interface and Method implementation strategy
        string interfaceToImplement;
        if (useGenericInterface)
        {
             interfaceToImplement = operation switch
            {
                "Get" => $"ISearchRepository<Search{methodName}Request>", 
                "GetById" => "IFindRepository<int>", 
                "Post" => $"IRepository<{methodName}Request>", 
                "Put" => $"IRepository<Update{methodName}Request>", 
                "Delete" => "IRepository<int>", 
                _ => throw new ArgumentException("Método no soportado")
            };
        }
        else
        {
            // Legacy Interface Names
             interfaceToImplement = operation switch
            {
                "Get" => $"ISearch{methodName}Repository",
                "GetById" => $"IFind{methodName}Repository",
                "Post" => $"I{methodName}Repository",
                "Put" => $"IUpdate{methodName}Repository",
                "Delete" => $"IDelete{methodName}Repository",
                _ => throw new ArgumentException("Método no soportado")
            };
        }

        var sb = new StringBuilder($@"
            using Dapper;
            using Domain.Shared.Interface.Base; 
            using Domain.{moduleName}.Entities;
            using Domain.Shared.Entities.Responses;
            {(!useGenericInterface ? $"using Domain.{moduleName}.Interfaces;" : "")}
            using {dbName}.Connections;
            using {dbName}.Repositories.Shared.Parser;
            using {dbName}.Repositories.{moduleName}.Dto;

            namespace {dbName}.Repositories.{moduleName};

            public class {className}({dbName}Connection connection) : {dbName}Repository(connection), {interfaceToImplement}
            {{
            ");

        sb.AppendLine(GenerateMethodCode(operation, moduleName, entityName, methodName, aiResult, useGenericInterface));
        sb.AppendLine("}");

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private static string GenerateMethodCode(string operation, string moduleName, string entityName, string methodName, SPAnalysisResult? aiResult, bool useGenericInterface)
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
        
        
        string implMethodName = "";
        
        return operation switch
        {
            // 🔍 GET
            "Get" => $@"
    public async Task<{(useGenericInterface ? "IEnumerable<object>" : "object")}> {(useGenericInterface ? "Search" : $"Search{methodName}")}(Search{methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryAsync<{entityName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return GenericListMapper.ParseCollection(response, dto => new {{
                    {mapperBlock}
        }});
    }}",

            // 🔎 GET BY ID 
            "GetById" => $@"
    public async Task<{(useGenericInterface ? "object?" : "object")}> {(useGenericInterface ? "Find" : $"Find{methodName}")}(int code)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock ?? "Code = code"}  
        }};
        var response = await cn.QueryFirstOrDefaultAsync<{entityName}Dto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        {(useGenericInterface ? "if (response == null) return null;" : "if (response == null) return null;")} 
        return GenericListMapper.Parse(response, dto => new {{
               {mapperBlock}
        }});
    }}",

            // 💾 POST 
            "Post" => $@"
    public async Task<Response> {(useGenericInterface ? "Execute" : $"{methodName}")}({methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}",

            // 🛠️ PUT
            "Put" => $@"
    public async Task<{(useGenericInterface ? "object" : "Response")}> {(useGenericInterface ? "Execute" : $"Update{methodName}")}(Update{methodName}Request request)
    {{
        using var cn = Connection();
        var parameters = new {{
                {paramBlock}
        }};
        var response = await cn.QueryFirstOrDefaultAsync<ResponseDto>({spName}, parameters, commandType: System.Data.CommandType.StoredProcedure);
        return ResponseParser.Make(response);
    }}",

            // ❌ DELETE 
            "Delete" => $@"
    public async Task<Response> {(useGenericInterface ? "Execute" : $"Delete{methodName}")}(int code)
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
    private static void GenerateOrUpdateDto(string dtoPath, string dbName, string moduleNamespace, string entityName, SPAnalysisResult? aiResult)
    {
        var className = $"{entityName}Dto";
        var dtoFields = aiResult.DTOFields ?? new();
        // Si no existe, se crea completo
        if (!File.Exists(dtoPath))
        {
            var content = $@"
using System;

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
