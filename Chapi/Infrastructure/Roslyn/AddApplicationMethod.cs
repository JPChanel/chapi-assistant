using Chapi.Infrastructure.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager;
using Chapi.Infrastructure.Persistence.Rollbacks;
using static Chapi.Infrastructure.Roslyn.GenerationStandards;

namespace Chapi.Infrastructure.Roslyn;

public static class AddApplicationMethod
{
    public static RollbackEntry Add(string appPath, string moduleName, string operation, string mName, RollbackEntry rollbackEntry = null, bool useGenericRepository = false)
    {
        Msg.Assistant("?? Procesando Application...");

        // 1. OBTENER CONFIGURACIÓN
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"?? Operación no soportada en Application: {operation}");
            return rollbackEntry;
        }

        // 2. GENERAR NOMBRES
        // Calcular segmento limpio del módulo para usar en namespace
        string cleanModule = moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        string lastModuleSegment = cleanModule.Split('.').Last();

        // Limpiar mName (MethodName)
        string cleanMethodName = mName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();

        // Si usa repositorio genérico (EndPoints Modernos), agregamos sufijo "Service"
        // Usamos el nombre del MÉTODO (Subject) para la clase, para permitir múltiples servicios en un módulo
        // Ejemplo: Config="Search{0}" -> {0}=Cargo -> SearchCargoService
        string classNameBase = FormatPattern(config.ApplicationClassNamePattern, cleanMethodName);
        if (useGenericRepository) classNameBase += "Service"; 
        
        var fileName = $"{classNameBase}.cs";
        var filePath = Path.Combine(appPath, fileName);

        if (!Directory.Exists(appPath))
            Directory.CreateDirectory(appPath);

        // 3. SI EL ARCHIVO NO EXISTE
        bool fileExisted = File.Exists(filePath);
        string originalContent = fileExisted ? File.ReadAllText(filePath) : null;

        if (!fileExisted)
        {
            var fileContent = GenerateNewAppClass(config, mName, cleanModule, operation, useGenericRepository);
            File.WriteAllText(filePath, fileContent);
            Msg.Assistant($"? Clase de aplicación creada: {fileName}");

            if (rollbackEntry != null)
                RollbackManager.RecordFileCreation(rollbackEntry, filePath);
            return rollbackEntry;
        }
        
        // RECUPERAR CONTENIDO Y AGREGAR MÉTODO SI YA EXISTE
        if (rollbackEntry != null)
             RollbackManager.RecordFileModification(rollbackEntry, filePath, originalContent);

        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classNode == null) return rollbackEntry;

        var targetMethodName = FormatPattern(config.ApplicationMethodNamePattern, mName);
        if (classNode.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == targetMethodName))
        {
            Msg.Assistant($"?? Ya existe el método '{targetMethodName}' en {fileName}");
            return rollbackEntry;
        }

        var newMethod = GenerateAppMethod(config, mName, useGenericRepository);
        var newClass = classNode.AddMembers(newMethod);
        
        var newRoot = root.ReplaceNode(classNode, newClass);
        File.WriteAllText(filePath, newRoot.NormalizeWhitespace().ToFullString());
        Msg.Assistant($"? Método '{targetMethodName}' agregado en {fileName}");
        return rollbackEntry;
    }

    private static string GenerateNewAppClass(GenerationStandards.OperationConfig config, string name, string cleanModule, string operation, bool useGenericRepository)
    {
        string lastModuleSegment = cleanModule.Split('.').Last();
        string cleanMethodName = name.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        
        var className = FormatPattern(config.ApplicationClassNamePattern, cleanMethodName);
        if (useGenericRepository) className += "Service";
        
        string namespaceName = $"Application.{cleanModule}";

        // Dependencies
        string repositoryInterface;
        string repositoryVar = "repository";
        
        // Imports strings
        string importInterfaces = $"using Domain.{cleanModule}.Interfaces;";

        if (useGenericRepository)
        {
            // NEW STYLE: IRepository<TReq, TRes>
            // Debe formatearse para incluir el nombre del DTO, ej: ISearchRepository<SearchUsuariosRequest, object>
             repositoryInterface = FormatPattern(config.GenericRepositoryInterfacePattern, cleanMethodName);
             
             // Si usamos genéricos, NO necesitamos la interfase específica del dominio viejo
             importInterfaces = ""; // Clean import
        }
        else
        {
            // LEGACY STYLE: INameRepository
            repositoryInterface = FormatPattern(config.ApplicationInterfaceNamePattern, name);
        }

        // Method Code
        string methodCode = GenerateAppMethodString(config, cleanMethodName, useGenericRepository);

        return $@"using Domain.Shared;
using Domain.Shared.Interface.Base;
using Domain.{cleanModule}.Entities;
{importInterfaces}
using Domain.Shared.Entities.Responses;
using System.Threading.Tasks;

namespace {namespaceName};

public class {className}({repositoryInterface} {repositoryVar}) 
{{
    {methodCode}
}}
";
    }

    private static MethodDeclarationSyntax GenerateAppMethod(GenerationStandards.OperationConfig config, string name, bool useGenericRepository)
    {
        string cleanMethodName = name.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        string methodCode = GenerateAppMethodString(config, cleanMethodName, useGenericRepository);
        // Parse the string into a MethodDeclarationSyntax
        // This helper simplifies mixing string templates with Roslyn nodes
        var tree = CSharpSyntaxTree.ParseText($"class C {{ {methodCode} }}");
        return tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
    }

    private static string GenerateAppMethodString(GenerationStandards.OperationConfig config, string cleanMethodName, bool useGenericRepository)
    {
        var methodName = FormatPattern(config.ApplicationMethodNamePattern, cleanMethodName);
        var requestName = FormatPattern(config.RequestDtoNamePattern, cleanMethodName); 
        
        // For Generic Repo (Endpoints), we use Domain DTO directly usually, or maintain legacy DTO pattern?
        // User said "use existing Domain models".
        // If legacy DTO pattern name is empty or complex, we might default to Object or dynamic.
        // Let's assume for Service layer we still pass 'request'.
        
        string repoCall;
        if (useGenericRepository)
        {
            // repo.Search(request)
            string genericMethod = config.GenericRepositoryMethodNamePattern;
            repoCall = $"repository.{genericMethod}(request)";
        }
        else
        {
            string legacyMethod = FormatPattern(config.RepositoryMethodNamePattern, cleanMethodName);
            repoCall = $"repository.{legacyMethod}(request)";
        }

        return $@"
    public async Task<object> {methodName}({requestName} request)
    {{
        return await {repoCall};
    }}";
    }


}



