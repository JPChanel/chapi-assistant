using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using static Chapi.Helper.RollbackManager;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Chapi.Helper.Roslyn;

public static class AddApiControllerMethod
{
    public static RollbackEntry Add(string apiPath, string moduleName, string operation, string methodName, RollbackEntry rollbackEntry = null)
    {
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            Msg.Assistant($"⚠️ Operación no soportada: {operation}");
            return rollbackEntry;
        }

        var controllerName = $"{moduleName}Controller";
        var fileName = controllerName + ".cs";

        var filePath = Path.Combine(apiPath, fileName);
        bool fileExisted = File.Exists(filePath);
        string originalContent = fileExisted ? File.ReadAllText(filePath) : null;
        if (!fileExisted)
        {
            GenerateBaseController(
                apiPath,
                fileName,
                moduleName,
                @namespace: "Http.Controllers"
            );
            // 📝 REGISTRAR CREACIÓN DE ARCHIVO
            if (rollbackEntry != null)
            {
                RollbackManager.RecordFileCreation(rollbackEntry, filePath);
            }
        }
        else if (rollbackEntry != null)
        {
            // 📝 REGISTRAR MODIFICACIÓN DE ARCHIVO
            RollbackManager.RecordFileModification(rollbackEntry, filePath, originalContent);
        }

        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        var classNode = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == controllerName);

        if (classNode == null) return rollbackEntry;

        string newMethodName = FormatPattern(config.ControllerMethodNamePattern, methodName);

        if (classNode.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == newMethodName))
        {
            Msg.Assistant($"ℹ️ Ya existe el método '{newMethodName}' en {fileName}");
            return rollbackEntry;
        }

        var classNodeWithDependency = AddDependencyToConstructor(classNode, operation, methodName);
        var newMethodNode = GenerateControllerMethod(operation, methodName);
        var lastMethod = classNodeWithDependency.Members.LastOrDefault(m => m is MethodDeclarationSyntax);

        var finalClassNode = lastMethod != null
            ? classNodeWithDependency.InsertNodesAfter(lastMethod, new[] { newMethodNode })
            : classNodeWithDependency.AddMembers(newMethodNode);

        var newRoot = root.ReplaceNode(classNode, finalClassNode);
        File.WriteAllText(filePath, newRoot.NormalizeWhitespace().ToFullString());
        Msg.Assistant($"✅ Método '{newMethodName}' agregado a {fileName}");
        return rollbackEntry;
    }

    private static ClassDeclarationSyntax AddDependencyToConstructor(
        ClassDeclarationSyntax classNode,
        string operation,
        string moduleName)
    {
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config) ||
            string.IsNullOrEmpty(config.DependencyTypePattern))
            return classNode;

        string dependencyType = FormatPattern(config.DependencyTypePattern, moduleName);
        string dependencyName = FormatPattern(config.DependencyNamePattern, moduleName);

        var newParam = Parameter(Identifier(dependencyName))
            .WithType(ParseTypeName(dependencyType));

        var paramList = classNode.ParameterList;

        if (paramList == null)
        {
            var newParamList = ParameterList(SingletonSeparatedList(newParam));
            return classNode.WithParameterList(newParamList);
        }

        if (paramList.Parameters.Any(p => p.Identifier.Text == dependencyName))
            return classNode;

        var updatedParamList = paramList.AddParameters(newParam);
        return classNode.WithParameterList(updatedParamList);
    }

    private static MethodDeclarationSyntax GenerateControllerMethod(string operation, string methodName)
    {
        if (!GenerationStandards.OperationConfigs.TryGetValue(operation.ToLower(), out var config))
        {
            throw new NotImplementedException($"Operación no implementada: {operation}");
        }

        string controllerMethodName = FormatPattern(config.ControllerMethodNamePattern, methodName);
        string bodyText = FormatPattern(config.RequestBody, methodName);

        var method = MethodDeclaration(ParseTypeName("Task<object>"), controllerMethodName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.AsyncKeyword))
            .AddAttributeLists(AttributeList().AddAttributes(
                Attribute(ParseName(config.HttpAttributeName))
            ))
            .WithBody(Block(ParseStatement(bodyText)));

        switch (operation.ToLower())
        {
            case "get":
                return method.WithParameterList(
                    CreateParameterList($"Search{methodName}Request", "request", "FromQuery"));

            case "post":
                return method.WithParameterList(
                    CreateParameterList($"{methodName}Request", "request", "FromBody"));

            case "put":
                return method.WithParameterList(
                    CreateParameterList($"{methodName}Request", "request", "FromBody"));

            case "getbyid":
                var methodWithRoute = method.WithAttributeLists(
                    List(new[] {
                        AttributeList().AddAttributes(
                            Attribute(ParseName("HttpGet"))
                                .WithArgumentList(AttributeArgumentList(
                                    SingletonSeparatedList(
                                        AttributeArgument(ParseExpression("\"{code}\""))
                                    )
                                ))
                        )
                    })
                );
                return methodWithRoute.WithParameterList(
                    CreateParameterList("int", "code", null));

            case "delete":
                return method.WithParameterList(CreateParameterList("int", "id", null));
        }

        return method;
    }

    private static ParameterListSyntax CreateParameterList(string type, string name, string attribute)
    {
        var parameter = Parameter(Identifier(name))
            .WithType(ParseTypeName(type));

        if (attribute != null)
        {
            parameter = parameter.WithAttributeLists(
                SingletonList(AttributeList().AddAttributes(
                    Attribute(ParseName(attribute))
                ))
            );
        }

        return ParameterList(SingletonSeparatedList(parameter));
    }

    private static string FormatPattern(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        var tempPattern = pattern.Replace("{0:lower}", value.ToLower());
        return string.Format(tempPattern, value);
    }

    // ✅ MEJORAR: Generar controller base completo con using statements y namespaces necesarios
    public static void GenerateBaseController(
        string controllerDirectory,
        string fileName,
        string moduleName,
        string @namespace)
    {
        Directory.CreateDirectory(controllerDirectory);
        var className = Path.GetFileNameWithoutExtension(fileName);

        // ✅ AGREGAR TODOS LOS USING NECESARIOS
        var usings = new[]
        {
            UsingDirective(ParseName("Microsoft.AspNetCore.Mvc")),
            UsingDirective(ParseName($"Application.{moduleName}")),
            UsingDirective(ParseName("Microsoft.AspNetCore.Authorization")),
            UsingDirective(ParseName($"Domain.{moduleName}.Entities"))
        };

        var apiControllerAttr = AttributeList(
            SingletonSeparatedList(
                Attribute(IdentifierName("ApiController"))
            )
        );

        var routeAttr = AttributeList(
            SingletonSeparatedList(
                Attribute(IdentifierName("Route"))
                    .WithArgumentList(
                        AttributeArgumentList(
                            SingletonSeparatedList(
                                AttributeArgument(
                                    LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        Literal("[controller]")
                                    )
                                )
                            )
                        )
                    )
            )
        );

        var authorizeAttr = AttributeList(
            SingletonSeparatedList(
                Attribute(IdentifierName("Authorize"))
            )
        );

        var classDeclaration = ClassDeclaration(className)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithBaseList(
                BaseList(
                    SingletonSeparatedList<BaseTypeSyntax>(
                        SimpleBaseType(IdentifierName("ControllerBase"))
                    )
                )
            )
            .WithAttributeLists(List(new[] { apiControllerAttr, routeAttr, authorizeAttr }))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        var namespaceDecl = NamespaceDeclaration(ParseName(@namespace))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(classDeclaration));

        var compilationUnit = CompilationUnit()
            .WithUsings(List(usings))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(namespaceDecl))
            .NormalizeWhitespace();

        var code = compilationUnit.ToFullString();
        var filePath = Path.Combine(controllerDirectory, fileName);
        File.WriteAllText(filePath, code);

        Msg.Assistant($"✅ Controller base creado: {fileName}");
    }
}