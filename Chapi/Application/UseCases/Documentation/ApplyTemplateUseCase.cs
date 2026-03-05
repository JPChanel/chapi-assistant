using Chapi.Domain.Documentation;

namespace Chapi.Application.UseCases.Documentation;

/// <summary>
/// Devuelve las secciones base para una plantilla de documento técnico.
/// Centraliza la definición de plantillas fuera del ViewModel.
/// </summary>
public class ApplyTemplateUseCase
{
    public (string DocumentTitle, IEnumerable<DocSection> Sections) Execute(DocTemplate template)
    {
        return template switch
        {
            DocTemplate.ModeloSoftware => ("Modelo de Software", GetModeloSoftwareSections()),
            DocTemplate.DisenoSistema => ("Diseño del Sistema de Información", GetDisenoSistemaSections()),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }

    private static IEnumerable<DocSection> GetModeloSoftwareSections() =>
    [
        new DocSection { Title = "Introducción", Type = DocSectionType.Text },
        new DocSection { Title = "Objetivos", Type = DocSectionType.Text },
        new DocSection { Title = "Alcance", Type = DocSectionType.Text },
        new DocSection
        {
            Title = "Diagrama de Paquetes",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.Mermaid,
            DiagramCode = "graph LR\n  A[Presentación] --> B[Aplicación]\n  B --> C[Dominio]\n  B --> D[Infraestructura]"
        },
        new DocSection
        {
            Title = "Diagrama de Actores",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.PlantUml,
            DiagramCode = "@startuml\nactor Usuario\nactor Administrador\n@enduml"
        },
        new DocSection
        {
            Title = "Casos de Uso",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.PlantUml,
            DiagramCode = "@startuml\nactor Usuario\nusecase \"Iniciar Sesión\" as UC1\nUsuario --> UC1\n@enduml"
        },
        new DocSection
        {
            Title = "Diagrama de Actividad",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.Mermaid,
            DiagramCode = "flowchart TD\n  A([Inicio]) --> B[Acción]\n  B --> C([Fin])"
        },
        new DocSection
        {
            Title = "Diagrama de Secuencia",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.Mermaid,
            DiagramCode = "sequenceDiagram\n  Usuario->>Sistema: Solicitud\n  Sistema-->>Usuario: Respuesta"
        },
    ];

    private static IEnumerable<DocSection> GetDisenoSistemaSections() =>
    [
        new DocSection { Title = "Introducción", Type = DocSectionType.Text },
        new DocSection { Title = "Objetivos", Type = DocSectionType.Text },
        new DocSection { Title = "Alcance", Type = DocSectionType.Text },
        new DocSection { Title = "Arquitectura del Sistema", Type = DocSectionType.Text },
        new DocSection
        {
            Title = "Diagrama de Componentes",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.PlantUml,
            DiagramCode = "@startuml\ncomponent [Frontend]\ncomponent [Backend]\ncomponent [Base de Datos]\n[Frontend] --> [Backend]\n[Backend] --> [Base de Datos]\n@enduml"
        },
        new DocSection
        {
            Title = "Diagrama de Clases",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.Mermaid,
            DiagramCode = "classDiagram\n  class Entidad {\n    +id: int\n    +nombre: string\n  }"
        },
        new DocSection
        {
            Title = "Diagrama Entidad - Relación",
            Type = DocSectionType.Diagram,
            DiagramFormat = DiagramFormat.Mermaid,
            DiagramCode = "erDiagram\n  USUARIO ||--o{ PEDIDO : realiza\n  PEDIDO ||--|{ ITEM : contiene"
        },
        new DocSection
        {
            Title = "Diccionario de Datos",
            Type = DocSectionType.Table,
            Content = "| Campo | Tipo | PK | Descripción |\n|---|---|---|---|\n| id | INT | ✅ | Identificador único |"
        },
    ];
}
