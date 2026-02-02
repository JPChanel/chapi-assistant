namespace Chapi.Infrastructure.Roslyn;

public static class GenerationStandards
{
    // AMPLIAMOS EL RECORD CON LAS NUEVAS PROPIEDADES
    public record OperationConfig
    {
        // Propiedades del Controller (como antes)
        public string ControllerMethodNamePattern { get; init; }
        public string DependencyTypePattern { get; init; }
        public string DependencyNamePattern { get; init; }
        public string AppServiceMethodPattern { get; init; }
        public string HttpAttributeName { get; init; }
        public string RequestBody { get; init; }

        // --- NUEVAS PROPIEDADES PARA LA CAPA DE APLICACIÓN ---
        public string ApplicationClassNamePattern { get; init; }
        public string ApplicationMethodNamePattern { get; init; }
        public string ApplicationInterfaceNamePattern { get; init; } // Legacy: ISearch{0}Repository
        public string GenericRepositoryInterfacePattern { get; init; } // Modern: ISearchRepository<Req, Res>
        public string RepositoryMethodNamePattern { get; init; } 
        public string GenericRepositoryMethodNamePattern { get; init; } 
        public string RequestDtoNamePattern { get; init; }
        
        // --- NUEVAS PROPIEDADES PARA ARDALIS ENDPOINTS ---
        public string EndpointFileName { get; init; } 
        public string EndpointRequestClassPattern { get; init; } 

        public string RepositoryClassNamePattern { get; init; }
        public string RepositoryNamespaceTag { get; init; }
    }

    // EL DICCIONARIO AHORA ES PÚBLICO Y ESTÁTICO EN ESTA CLASE
    public static readonly Dictionary<string, OperationConfig> OperationConfigs = new()
    {
        ["get"] = new OperationConfig
        {
            ControllerMethodNamePattern = "Get{0}",
            HttpAttributeName = "HttpGet",
            DependencyTypePattern = "Search{0}",
            DependencyNamePattern = "search{0}",
            AppServiceMethodPattern = "search{0}",
            RequestBody = "var response = await search{0}.search{0}(request); return Results.Ok(new {{ data = response }});",

            ApplicationClassNamePattern = "Search{0}",
            ApplicationMethodNamePattern = "search{0}",
            ApplicationInterfaceNamePattern = "ISearch{0}Repository",
            GenericRepositoryInterfacePattern = "ISearchRepository<Search{0}Request>", 
            RepositoryMethodNamePattern = "Search{0}", 
            GenericRepositoryMethodNamePattern = "Search", 
            RequestDtoNamePattern = "Search{0}Request",
            
            EndpointFileName = "Search",
            EndpointRequestClassPattern = "Search{0}Request",

            RepositoryClassNamePattern = "Search{0}Repository",
            RepositoryNamespaceTag = "Search"
        },
        ["post"] = new OperationConfig
        {
            ControllerMethodNamePattern = "{0}",
            HttpAttributeName = "HttpPost",
            DependencyTypePattern = "{0}",
            DependencyNamePattern = "{0:lower}",
            AppServiceMethodPattern = "{0:lower}",
            RequestBody = "var response = await {0:lower}.{0:lower}(request); return Results.Ok(new {{ data = response }});",

            ApplicationClassNamePattern = "{0}",
            ApplicationMethodNamePattern = "{0}",
            ApplicationInterfaceNamePattern = "I{0}Repository", // Legacy
            GenericRepositoryInterfacePattern = "IRepository<{0}Request>", // Modern
            RepositoryMethodNamePattern = "{0}", // Legacy
            GenericRepositoryMethodNamePattern = "Execute", // Modern
            RequestDtoNamePattern = "{0}Request",
            

            EndpointFileName = "Execute", 
            EndpointRequestClassPattern = "{0}Request",

            RepositoryClassNamePattern = "{0}Repository",
            RepositoryNamespaceTag = "Execute"
        },
        ["getbyid"] = new OperationConfig
        {
            ControllerMethodNamePattern = "GetById{0}",
            HttpAttributeName = "HttpGet",
            DependencyTypePattern = "Find{0}",
            DependencyNamePattern = "find{0}",
            AppServiceMethodPattern = "Find{0}ById",
            RequestBody = "var response = await find{0}.Find{0}ById(code); return Results.Ok(new {{ data = response }});",

            ApplicationClassNamePattern = "Find{0}",
            ApplicationMethodNamePattern = "Find{0}ById",
            ApplicationInterfaceNamePattern = "IFind{0}Repository", // Legacy
            GenericRepositoryInterfacePattern = "IFindRepository<int>", // Modern
            RepositoryMethodNamePattern = "Find{0}", // Legacy
            GenericRepositoryMethodNamePattern = "Find", // Modern
            RequestDtoNamePattern = "int",
            
    
            EndpointFileName = "GetById",
            EndpointRequestClassPattern = "int", 
            RepositoryClassNamePattern = "Find{0}Repository",
            RepositoryNamespaceTag = "Find"
        }
    };

    public static string FormatPattern(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        var tempPattern = pattern.Replace("{0:lower}", value.ToLower());
        return string.Format(tempPattern, value);
    }
}

