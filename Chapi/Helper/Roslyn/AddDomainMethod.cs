using Chapi.Model;
using Chapi.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Helper.RollbackManager;

namespace Chapi.Helper.Roslyn;

public class AddDomainMethod
{
    public static async Task<RollbackEntry> Add(string modulePath, string moduleName, string operation, string methodName, RollbackManager.RollbackEntry rollbackEntry = null, SPAnalysisResult? aiResult = null)
    {

        string entitiesPath = Path.Combine(modulePath, "Entities");
        string interfacesPath = Path.Combine(modulePath, "Interfaces");

        Directory.CreateDirectory(entitiesPath);
        Directory.CreateDirectory(interfacesPath);
        operation = operation == "Get" ? "Search" : operation == "GetById" ? "Find" : operation;

        Msg.Assistant($"🔧 Agregando '{operation}' en Domain.{methodName}...");

        string ns = $"Domain.{moduleName}";

        // Preparar nombres
        string interfaceName = "";
        string methodSignature = "";
        string requestClass = "";
        string requestPath = "";

        switch (operation.ToLower())
        {
            case "search":
                interfaceName = $"ISearch{methodName}Repository";
                requestClass = $"Search{methodName}Request";
                methodSignature = $"Task<object> Search{methodName}({requestClass} request);";
                requestPath = Path.Combine(entitiesPath, $"{requestClass}.cs");
                break;

            case "post":
                interfaceName = $"I{methodName}Repository";
                requestClass = $"{methodName}Request";
                methodSignature = $"Task<Response> {methodName}({requestClass} request);";
                requestPath = Path.Combine(entitiesPath, $"{requestClass}.cs");
                break;

            case "find":
                interfaceName = $"IFind{methodName}Repository";
                methodSignature = $"Task<object> Find{methodName}(int code);";
                break;

            case "put":
                interfaceName = $"IUpdate{methodName}Repository";
                requestClass = $"Update{methodName}Request";
                methodSignature = $"Task<Response> Update{methodName}({requestClass} request);";
                requestPath = Path.Combine(entitiesPath, $"{requestClass}.cs");
                break;

            case "delete":
                interfaceName = $"IDelete{methodName}Repository";
                methodSignature = $"Task<Response> Delete{methodName}(int code);";
                break;

            default:
                Msg.Assistant($"⚠ Funcionalidad '{operation}' no soportada.");
                DialogService.ShowTrayNotification("Error", $"⚠ Funcionalidad '{operation}' no soportada.");
                return rollbackEntry;
        }

        if (!string.IsNullOrEmpty(requestPath) && !File.Exists(requestPath))
        {
            var requestDataType = aiResult?.RequestParameters ?? new();
            File.WriteAllText(requestPath, $@"namespace {ns}.Entities;
            public class {requestClass} {{  {string.Join("\n    ", requestDataType)} }}");
            Msg.Assistant($"✅ Clase de entidad '{requestClass}' creada.");
            if (rollbackEntry != null)
            {
                RollbackManager.RecordFileCreation(rollbackEntry, requestPath);
            }
        }

        string interfacePath = Path.Combine(interfacesPath, $"{interfaceName}.cs");
        // 📝 REGISTRAR CAMBIOS EN INTERFAZ
        bool interfaceExisted = File.Exists(interfacePath);
        string originalContent = interfaceExisted ? await File.ReadAllTextAsync(interfacePath) : null;


        await EnsureInterfaceAsync(interfacePath, interfaceName, methodSignature, ns);
        if (rollbackEntry != null)
        {
            if (!interfaceExisted)
            {
                RollbackManager.RecordFileCreation(rollbackEntry, interfacePath);
            }
            else if (originalContent != null)
            {
                RollbackManager.RecordFileModification(rollbackEntry, interfacePath, originalContent);
            }
        }
        Msg.Assistant($"✅ Método '{methodSignature.Trim()}' asegurado en {interfaceName}");
        return rollbackEntry;
    }
    public static async Task EnsureInterfaceAsync(string filePath, string interfaceName, string methodSignature, string @namespace)
    {
        if (!File.Exists(filePath))
        {
            // Crear interfaz si no existe
            File.WriteAllText(filePath, $@"using {@namespace}.Entities; 
            using Domain.Shared.Entities;
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
}
