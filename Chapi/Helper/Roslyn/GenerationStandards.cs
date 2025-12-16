namespace Chapi.Helper.Roslyn;

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
        public string RepositoryMethodNamePattern { get; init; } // Legacy: Search{0}
        public string GenericRepositoryMethodNamePattern { get; init; } // Modern: Search
        public string RequestDtoNamePattern { get; init; }
        
        // --- NUEVAS PROPIEDADES PARA ARDALIS ENDPOINTS ---
        public string EndpointFileName { get; init; } // Nombre del archivo/clase del Endpoint (ej: Search, Execute)
        public string EndpointRequestClassPattern { get; init; } // El DTO de Request que viene de Domain (ej: Search{0}Request)
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

            // --- REGLAS PARA LA CAPA DE APLICACIÓN ---
            ApplicationClassNamePattern = "Search{0}",
            ApplicationMethodNamePattern = "Search{0}",
            ApplicationInterfaceNamePattern = "ISearch{0}Repository", // Legacy
            GenericRepositoryInterfacePattern = "ISearchRepository<Search{0}Request>", // Modern
            RepositoryMethodNamePattern = "Search{0}", // Legacy
            GenericRepositoryMethodNamePattern = "Search", // Modern
            RequestDtoNamePattern = "Search{0}Request",
            
            // --- ENDPOINT ---
            EndpointFileName = "Search",
            EndpointRequestClassPattern = "Search{0}Request"
        },
        ["post"] = new OperationConfig
        {
            ControllerMethodNamePattern = "{0}",
            HttpAttributeName = "HttpPost",
            DependencyTypePattern = "{0}",
            DependencyNamePattern = "{0:lower}",
            AppServiceMethodPattern = "{0:lower}",
            RequestBody = "var response = await {0:lower}.{0:lower}(request); return Results.Ok(new {{ data = response }});",

            // --- REGLAS PARA LA CAPA DE APLICACIÓN ---
            ApplicationClassNamePattern = "{0}",
            ApplicationMethodNamePattern = "{0}",
            ApplicationInterfaceNamePattern = "I{0}Repository", // Legacy
            GenericRepositoryInterfacePattern = "IRepository<{0}Request>", // Modern
            RepositoryMethodNamePattern = "{0}", // Legacy
            GenericRepositoryMethodNamePattern = "Execute", // Modern
            RequestDtoNamePattern = "{0}Request",
            
            // --- ENDPOINT ---
            EndpointFileName = "Execute", 
            EndpointRequestClassPattern = "{0}Request"
        },
        ["getbyid"] = new OperationConfig
        {
            ControllerMethodNamePattern = "GetById{0}",
            HttpAttributeName = "HttpGet",
            DependencyTypePattern = "Find{0}",
            DependencyNamePattern = "find{0}",
            AppServiceMethodPattern = "Find{0}ById",
            RequestBody = "var response = await find{0}.Find{0}ById(code); return Results.Ok(new {{ data = response }});",

            // --- REGLAS PARA LA CAPA DE APLICACIÓN ---
            ApplicationClassNamePattern = "Find{0}",
            ApplicationMethodNamePattern = "Find{0}ById",
            ApplicationInterfaceNamePattern = "IFind{0}Repository", // Legacy
            GenericRepositoryInterfacePattern = "IFindRepository<int>", // Modern
            RepositoryMethodNamePattern = "Find{0}", // Legacy
            GenericRepositoryMethodNamePattern = "Find", // Modern
            RequestDtoNamePattern = "int",
            
            // --- ENDPOINT ---
            EndpointFileName = "GetById",
            EndpointRequestClassPattern = "int" // Ojo: EndpointBase requiere clase, int no sirve.
            // Para GetById, usualmente no hay Request DTO complejo, pero Ardalis fuerza uno o usa int?
            // Si usamos .WithRequest<int>, funciona.
        }
    };
}
