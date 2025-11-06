using Chapi.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Helper.RollbackManager;
namespace Chapi.Helper.Roslyn;

public static class AddApplicationMethod
{
    public static RollbackEntry Add(string appPath, string moduleName, string operation, string mName, RollbackEntry rollbackEntry = null)
    {
        Msg.Assistant("🔧 Procesando Application...");

        // 1. OBTENER LA CONFIGURACIÓN DEL DICCIONARIO CENTRAL
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"⚠️ Operación no soportada en Application: {operation}");
            DialogService.ShowTrayNotification("Error", $"⚠️ Operación no soportada en Application: {operation}");
            return rollbackEntry;
        }

        // 2. GENERAR NOMBRES BASADO EN LA CONFIGURACIÓN (¡ADIÓS A LOS TERNARIOS!)
        var className = FormatPattern(config.ApplicationClassNamePattern, mName);
        var fileName = $"{className}.cs";

        var filePath = Path.Combine(appPath, fileName);

        if (!Directory.Exists(appPath))
            Directory.CreateDirectory(appPath);

        // 3. SI EL ARCHIVO NO EXISTE, GENERARLO USANDO LA PLANTILLA REFACTORIZADA
        bool fileExisted = File.Exists(filePath);
        string originalContent = fileExisted ? File.ReadAllText(filePath) : null;

        if (!fileExisted)
        {
            var fileContent = GenerateNewAppClass(config, mName, moduleName, operation);
            File.WriteAllText(filePath, fileContent);
            Msg.Assistant($"✅ Clase de aplicación creada: {fileName}");

            // 📝 REGISTRAR CREACIÓN DE ARCHIVO
            if (rollbackEntry != null)
            {
                RollbackManager.RecordFileCreation(rollbackEntry, filePath);
            }
            return rollbackEntry;
        }
        // 📝 REGISTRAR MODIFICACIÓN DE ARCHIVO
        if (rollbackEntry != null)
        {
            RollbackManager.RecordFileModification(rollbackEntry, filePath, originalContent);
        }
        // --- LÓGICA DE ROSLYN MEJORADA ---
        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classNode == null)
        {
            Msg.Assistant($"⚠️ No se encontró clase en {fileName}");
            DialogService.ShowTrayNotification("Error", $"⚠️ No se encontró clase en {fileName}");
            return rollbackEntry;
        }

        var targetMethodName = FormatPattern(config.ApplicationMethodNamePattern, mName);
        bool methodExists = classNode.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == targetMethodName);

        if (methodExists)
        {
            Msg.Assistant($"ℹ️ Ya existe el método '{targetMethodName}' en {fileName}");
            DialogService.ShowTrayNotification("Error", $"ℹ️ Ya existe el método '{targetMethodName}' en {fileName}");
            return rollbackEntry;
        }

        // 4. GENERAR EL MÉTODO USANDO LA CONFIGURACIÓN
        var newMethod = GenerateAppMethod(config, mName);

        // 5. INSERTAR EL NUEVO MÉTODO DESPUÉS DEL ÚLTIMO EXISTENTE
        var lastMethod = classNode.Members.LastOrDefault(m => m is MethodDeclarationSyntax);
        var newClass = lastMethod != null
            ? classNode.InsertNodesAfter(lastMethod, new[] { newMethod })
            : classNode.AddMembers(newMethod);

        var newRoot = root.ReplaceNode(classNode, newClass);
        File.WriteAllText(filePath, newRoot.NormalizeWhitespace().ToFullString());
        Msg.Assistant($"✅ Método '{targetMethodName}' agregado en {fileName}");
        return rollbackEntry;
    }

    private static string GenerateNewAppClass(GenerationStandards.OperationConfig config, string name, string module, string operation)
    {
        var className = FormatPattern(config.ApplicationClassNamePattern, name);
        var interfaceName = FormatPattern(config.ApplicationInterfaceNamePattern, name);
        var requestName = FormatPattern(config.RequestDtoNamePattern, name);
        var methodName = FormatPattern(config.ApplicationMethodNamePattern, name);
        var repositoryMethod = FormatPattern(config.RepositoryMethodNamePattern, name);
        var parameter = string.IsNullOrEmpty(requestName) ? "" : $"{requestName} request";

        parameter = operation == "GetById" ? requestName : parameter;

        var param = operation == "GetById" ? "code" : "request";

        // La plantilla ahora es 100% dirigida por la configuración
        return $@"
        using Domain.{module}.Interfaces;
        using Domain.{module}.Entities;

        namespace Application.{module};

        public class {className}({interfaceName} repository)
        {{
            public async Task<object> {methodName}({parameter})
            {{
                return await repository.{repositoryMethod}({param});
            }}
        }}".Trim();
    }

    private static MethodDeclarationSyntax GenerateAppMethod(GenerationStandards.OperationConfig config, string name)
    {
        var methodName = FormatPattern(config.ApplicationMethodNamePattern, name);
        var requestName = FormatPattern(config.RequestDtoNamePattern, name);
        var repositoryMethod = FormatPattern(config.RepositoryMethodNamePattern, name);

        var method = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("async Task<object>"), methodName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AsyncKeyword))
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ParseStatement($"return await repository.{repositoryMethod}(request);")
            ));

        if (!string.IsNullOrEmpty(requestName))
        {
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("request"))
                .WithType(SyntaxFactory.ParseTypeName(requestName));
            method = method.AddParameterListParameters(parameter);
        }

        return method;
    }

    // Función de ayuda para formatear (puedes moverla a la clase GenerationStandards)
    private static string FormatPattern(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        var tempPattern = pattern.Replace("{0:lower}", value.ToLower());
        return string.Format(tempPattern, value);
    }
}