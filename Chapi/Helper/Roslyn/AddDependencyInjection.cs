using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;

namespace Chapi.Helper.Roslyn;

public class AddDependencyInjection
{
    public static void Add(string dependencyInjectionPath, string methodName, IEnumerable<string> operations)
    {
        if (!File.Exists(dependencyInjectionPath))
        {
            Msg.Assistant($"⚠️ No se encontró DependencyInjection.cs en: {dependencyInjectionPath}");
            return;
        }

        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(dependencyInjectionPath));
        var root = tree.GetRoot();

        var compilationUnit = (CompilationUnitSyntax)root;
        var rewriter = new DependencyInjectionRewriter(methodName, operations);
        var newRoot = rewriter.Visit(compilationUnit);

        var formattedCode = newRoot.NormalizeWhitespace().ToFullString();
        File.WriteAllText(dependencyInjectionPath, formattedCode);
        Msg.Assistant($"✅ Servicios del módulo '{methodName}' inyectados correctamente.");
    }

    public class DependencyInjectionRewriter : CSharpSyntaxRewriter
    {
        private readonly string _methodName;
        private readonly List<string> _operations;
        private readonly List<string> _repositoryLines;
        private readonly List<string> _appServiceLines;

        public DependencyInjectionRewriter(string methodName, IEnumerable<string> operations)
        {
            _methodName = methodName;
            _operations = operations.Select(op => op.Trim()).ToList();
            _repositoryLines = new List<string>();
            _appServiceLines = new List<string>();

            foreach (var op in _operations)
            {
                // ✅ NORMALIZAR OPERACIONES (Get, GetById, Post, etc.)
                var normalizedOp = op.ToLower();

                switch (normalizedOp)
                {
                    case "get":
                        _repositoryLines.Add($"services.AddScoped<ISearch{_methodName}Repository, Search{_methodName}Repository>()");
                        _appServiceLines.Add($"services.AddScoped<Search{_methodName}>()");
                        break;

                    case "getbyid":
                        _repositoryLines.Add($"services.AddScoped<IFind{_methodName}Repository, Find{_methodName}Repository>()");
                        _appServiceLines.Add($"services.AddScoped<Find{_methodName}>()");
                        break;

                    case "post":
                        _repositoryLines.Add($"services.AddScoped<I{_methodName}Repository, {_methodName}Repository>()");
                        _appServiceLines.Add($"services.AddScoped<{_methodName}>()");
                        break;

                    case "put":
                        _repositoryLines.Add($"services.AddScoped<IUpdate{_methodName}Repository, Update{_methodName}Repository>()");
                        _appServiceLines.Add($"services.AddScoped<Update{_methodName}>()");
                        break;

                    case "delete":
                        _repositoryLines.Add($"services.AddScoped<IDelete{_methodName}Repository, Delete{_methodName}Repository>()");
                        _appServiceLines.Add($"services.AddScoped<Delete{_methodName}>()");
                        break;

                    default:
                        Msg.Assistant($"⚠️ Operación no reconocida para DI: {op}");
                        break;
                }
            }
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var methodName = node.Identifier.Text;

            if (methodName == "ConfigureRepositories")
            {
                return InsertLines(node, _repositoryLines);
            }
            else if (methodName == "ConfigureAppServices")
            {
                return InsertLines(node, _appServiceLines);
            }

            return base.VisitMethodDeclaration(node);
        }

        private MethodDeclarationSyntax InsertLines(MethodDeclarationSyntax method, List<string> linesToInsert)
        {
            if (method.Body == null) return method;

            var body = method.Body;

            // ✅ OBTENER LÍNEAS ACTUALES PARA EVITAR DUPLICADOS
            var currentLines = body.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(s => s.ToString().Trim().Replace(" ", "").Replace("\n", "").Replace("\r", ""))
                .ToList();

            // ✅ FILTRAR LÍNEAS QUE YA EXISTEN
            var newStatements = linesToInsert
                .Where(line =>
                {
                    var normalizedLine = line.Trim().Replace(" ", "").Replace("\n", "").Replace("\r", "");
                    return !currentLines.Any(c => c.Contains(normalizedLine) || normalizedLine.Contains(c));
                })
                .Select(line =>
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.ParseExpression(line)
                    )
                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                )
                .ToArray();

            if (newStatements.Length == 0)
            {
                Msg.Assistant($"ℹ️ Todas las dependencias ya están registradas en {method.Identifier.Text}");
                return method;
            }

            var updatedBody = body.AddStatements(newStatements);
            return method.WithBody(updatedBody);
        }
    }
}