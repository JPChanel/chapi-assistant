using Chapi.Domain.Models;
using Chapi.Infrastructure.Persistence.Rollbacks;
using Chapi.Infrastructure.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Infrastructure.Persistence.Rollbacks.RollbackManager;
using static Chapi.Infrastructure.Roslyn.GenerationStandards;

namespace Chapi.Infrastructure.Roslyn;

public class AddDomainMethod
{
    public static async Task<RollbackEntry> Add(string modulePath, string moduleName, string operation, string methodName, RollbackEntry rollbackEntry = null, SPAnalysisResult? aiResult = null, bool isArdalisStyle = false)
    {

        string entitiesPath = Path.Combine(modulePath, "Entities");
        string interfacesPath = Path.Combine(modulePath, "Interfaces");

        Directory.CreateDirectory(entitiesPath);
        if (!isArdalisStyle) Directory.CreateDirectory(interfacesPath);

        string entityName = moduleName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        string entityPath = Path.Combine(entitiesPath, $"{entityName}.cs");
        string ns = $"Domain.{moduleName.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')}";

        if (aiResult != null)
        {
            await GenerateOrUpdateEntity(entityPath, ns, entityName, aiResult, rollbackEntry);
        }

        Msg.Assistant($"Agregando '{operation}' en Domain.{methodName}...");

        var cleanMethodName = methodName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Last();
        if (cleanMethodName != methodName)
        {
            methodName = cleanMethodName;
            Msg.Assistant($"Nombre de método saneado a: {methodName}");
        }

        // Preparar nombres
        string interfaceName = "";
        string methodSignature = "";
        string requestClass = "";
        string requestPath = "";

        if (!OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"Operación '{operation}' no soportada.");
            return rollbackEntry;
        }

        string repoMethodName = isArdalisStyle ? config.GenericRepositoryMethodNamePattern : FormatPattern(config.RepositoryMethodNamePattern, methodName);
        requestClass = FormatPattern(config.EndpointRequestClassPattern, methodName);
        interfaceName = isArdalisStyle ? FormatPattern(config.GenericRepositoryInterfacePattern, methodName) : FormatPattern(config.ApplicationInterfaceNamePattern, methodName);

        string resultType = operation.ToLower().Contains("get") ? "object" : "Response";
        methodSignature = $"Task<{resultType}> {repoMethodName}({requestClass} request);";

        if (operation.ToLower() == "getbyid" || operation.ToLower() == "delete")
        {
            // Overwrite for simple types if not using complex request
            if (requestClass == "int") methodSignature = $"Task<{resultType}> {repoMethodName}(int code);";
        }

        requestPath = Path.Combine(entitiesPath, $"{requestClass}.cs");

        if (!string.IsNullOrEmpty(requestPath) && !File.Exists(requestPath))
        {
            var requestDataType = aiResult?.RequestParameters ?? new();
            File.WriteAllText(requestPath, $@"namespace {ns}.Entities;
            public class {requestClass} {{  {string.Join("\n    ", requestDataType)} }}");
            Msg.Assistant($"Clase de entidad '{requestClass}' creada.");
            if (rollbackEntry != null)
            {
                RollbackManager.RecordFileCreation(rollbackEntry, requestPath);
            }
        }

        if (isArdalisStyle)
        {
            return rollbackEntry; // Si usamos genéricos, no creamos interfaces específicas
        }

        string interfacePath = Path.Combine(interfacesPath, $"{interfaceName}.cs");
        bool interfaceExisted = File.Exists(interfacePath);
        string originalContent = interfaceExisted ? await File.ReadAllTextAsync(interfacePath) : null;

        await EnsureInterfaceAsync(interfacePath, interfaceName, methodSignature, ns);

        if (rollbackEntry != null)
        {
            if (!interfaceExisted)
                RollbackManager.RecordFileCreation(rollbackEntry, interfacePath);
            else if (originalContent != null)
                RollbackManager.RecordFileModification(rollbackEntry, interfacePath, originalContent);
        }

        Msg.Assistant($"Método '{methodSignature.Trim()}' asegurado en {interfaceName}");
        return rollbackEntry;
    }
    public static async Task EnsureInterfaceAsync(string filePath, string interfaceName, string methodSignature, string @namespace)
    {
        if (!File.Exists(filePath))
        {
            // Crear interfaz si no existe
            File.WriteAllText(filePath, $@"using {@namespace}.Entities; 
            using Domain.Shared.Entities;
            using Domain.Shared.Entities.Responses;
            
            namespace {@namespace}.Interfaces;
            public interface {interfaceName}
            {{
                {methodSignature}
            }}");
            return;
        }

        var code = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();
        var interfaceNode = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().FirstOrDefault();

        if (interfaceNode == null)
            return;

        // Verifica si el método ya existe
        if (interfaceNode.Members.Any(m => m.ToString().Contains(methodSignature.Split('(')[0].Trim())))
            return;

        var methodNode = SyntaxFactory.ParseMemberDeclaration(methodSignature)
            .WithLeadingTrivia(SyntaxFactory.Whitespace("\n    "));

        var updatedInterface = interfaceNode.AddMembers(methodNode);
        var newRoot = root.ReplaceNode(interfaceNode, updatedInterface);

        await File.WriteAllTextAsync(filePath, newRoot.NormalizeWhitespace().ToFullString());
    }

    private static async Task GenerateOrUpdateEntity(string filePath, string @namespace, string entityName, SPAnalysisResult aiResult, RollbackEntry rollbackEntry)
    {
        var fields = aiResult.DTOFields ?? new List<string>();
        if (!File.Exists(filePath))
        {
            var content = $@"namespace {@namespace}.Entities;

public class {entityName}
{{
    {string.Join("\n    ", fields)}
}}";
            File.WriteAllText(filePath, content);
            Msg.Assistant($"Entidad de dominio '{entityName}' creada.");
            if (rollbackEntry != null) RollbackManager.RecordFileCreation(rollbackEntry, filePath);
            return;
        }

        // Si existe, actualizar con nuevos campos
        var existingCode = await File.ReadAllTextAsync(filePath);
        if (rollbackEntry != null) RollbackManager.RecordFileModification(rollbackEntry, filePath, existingCode);

        var tree = CSharpSyntaxTree.ParseText(existingCode);
        var root = await tree.GetRootAsync();
        var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        if (classNode == null) return;

        var existingProps = classNode.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(p => p.Identifier.Text)
            .ToHashSet();

        var newMembers = fields
            .Select(f => SyntaxFactory.ParseMemberDeclaration(f))
            .Where(m => m is PropertyDeclarationSyntax prop && !existingProps.Contains(prop.Identifier.Text))
            .ToArray();

        if (newMembers.Any())
        {
            var updatedClass = classNode.AddMembers(newMembers);
            var newRoot = root.ReplaceNode(classNode, updatedClass);
            await File.WriteAllTextAsync(filePath, newRoot.NormalizeWhitespace().ToFullString());
            Msg.Assistant($"Entidad '{entityName}' actualizada con {newMembers.Length} campos nuevos.");
        }
    }
}



