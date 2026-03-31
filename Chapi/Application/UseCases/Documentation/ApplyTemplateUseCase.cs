using Chapi.Domain.Documentation;

namespace Chapi.Application.UseCases.Documentation;

/// <summary>
/// Devuelve las secciones base para una plantilla de documento tecnico.
/// Centraliza la definicion de plantillas fuera del ViewModel.
/// </summary>
public class ApplyTemplateUseCase
{
    public (string DocumentTitle, IEnumerable<DocSection> Sections) Execute(DocTemplate template)
    {
        return template switch
        {
            DocTemplate.ModeloSoftware => ("Modelo de Software", GetModeloSoftwareSections()),
            DocTemplate.DisenoSistema => ("Diseno del Sistema de Informacion", GetDisenoSistemaSections()),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }

    private static IEnumerable<DocSection> GetModeloSoftwareSections() =>
    [
        new DocSection { Order = 1, Title = "Introduccion", Type = DocSectionType.Text },
        new DocSection { Order = 2, Title = "Objetivos", Type = DocSectionType.Text },
        new DocSection { Order = 3, Title = "Alcance", Type = DocSectionType.Text },
        new DocSection { Order = 4, Title = "Diagrama de Paquetes / Vista Logica", Type = DocSectionType.Text },
        new DocSection { Order = 5, Title = "4.1 Listado de paquetes", Type = DocSectionType.Table },
        new DocSection { Order = 6, Title = "4.2 Presentacion", Type = DocSectionType.Image },
        new DocSection { Order = 7, Title = "4.3 Especificacion de paquetes", Type = DocSectionType.Table },
        new DocSection { Order = 8, Title = "Diagrama de Actores", Type = DocSectionType.Text },
        new DocSection { Order = 9, Title = "5.1 Listado de actores", Type = DocSectionType.Table },
        new DocSection { Order = 10, Title = "5.2 Diagrama de actores", Type = DocSectionType.Image },
        new DocSection { Order = 11, Title = "Diagrama de Casos de Uso", Type = DocSectionType.Text },
        new DocSection { Order = 12, Title = "6.1 Listado de Casos de Uso", Type = DocSectionType.Table },
        new DocSection { Order = 13, Title = "6.2 Especificacion de Casos de Uso", Type = DocSectionType.Table },
        new DocSection { Order = 14, Title = "Diagrama de Actividad", Type = DocSectionType.Diagram },
        new DocSection { Order = 15, Title = "Diagrama de Secuencia", Type = DocSectionType.Diagram },
        new DocSection { Order = 16, Title = "Diagrama de Estados", Type = DocSectionType.Diagram },
    ];

    private static IEnumerable<DocSection> GetDisenoSistemaSections() =>
    [
        new DocSection { Order = 1, Title = "Introduccion", Type = DocSectionType.Text },
        new DocSection { Order = 2, Title = "Objetivos", Type = DocSectionType.Text },
        new DocSection { Order = 3, Title = "Alcance", Type = DocSectionType.Text },
        new DocSection { Order = 4, Title = "Arquitectura del Sistema", Type = DocSectionType.Text },
        new DocSection { Order = 5, Title = "4.1 Descripcion de capas", Type = DocSectionType.Table },
        new DocSection { Order = 6, Title = "Diagrama de Componentes", Type = DocSectionType.Text },
        new DocSection { Order = 7, Title = "5.1 Diagrama de componentes", Type = DocSectionType.Image },
        new DocSection { Order = 8, Title = "5.2 Descripcion de componentes", Type = DocSectionType.Table },
        new DocSection { Order = 9, Title = "Diagrama de Clases", Type = DocSectionType.Text },
        new DocSection { Order = 10, Title = "6.1 Diagrama de clases", Type = DocSectionType.Image },
        new DocSection { Order = 11, Title = "6.2 Especificacion de clases", Type = DocSectionType.Table },
        new DocSection { Order = 12, Title = "Diagrama Entidad - Relacion", Type = DocSectionType.Diagram },
        new DocSection { Order = 13, Title = "Diccionario de Datos", Type = DocSectionType.Text },
        new DocSection { Order = 14, Title = "8.1 Listado de tablas", Type = DocSectionType.Table },
        new DocSection { Order = 15, Title = "8.2 Descripcion de tablas", Type = DocSectionType.Table },
        new DocSection { Order = 16, Title = "8.3 Listado de paquetes", Type = DocSectionType.Table },
        new DocSection { Order = 17, Title = "8.4 Listado de procedimientos", Type = DocSectionType.Table },
        new DocSection { Order = 18, Title = "8.5 Listado de vistas", Type = DocSectionType.Table },
        new DocSection { Order = 19, Title = "8.6 Listado de funciones", Type = DocSectionType.Table },
        new DocSection { Order = 20, Title = "8.7 Listado de indices", Type = DocSectionType.Table },
    ];
}
