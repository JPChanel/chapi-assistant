using Chapi.Application.UseCases.Git;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models.Assistant;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chapi.Application.Services.Assistant;

public class AssistantCapabilityRegistry : IAssistantCapabilityRegistry
{
    private readonly List<AssistantCapability> _capabilities = new();

    public AssistantCapabilityRegistry()
    {
        InitializeGitCapabilities();
    }

    private void InitializeGitCapabilities()
    {
        _capabilities.Add(new AssistantCapability
        {
            Id = "git.commit",
            Name = "Confirmar Cambios (Commit)",
            Description = "Guarda tus cambios locales en el historial del repositorio.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "commit", "confirmar", "guardar cambios", "hacer commit" },
            TargetUseCaseType = typeof(CommitChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.push",
            Name = "Subir Cambios (Push)",
            Description = "Envía tus commits locales al servidor remoto.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "push", "subir", "enviar cambios", "publicar" },
            TargetUseCaseType = typeof(PushChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.pull",
            Name = "Bajar Cambios (Pull)",
            Description = "Trae y fusiona los cambios del servidor remoto a tu copia local.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "pull", "bajar", "actualizar", "traer cambios" },
            TargetUseCaseType = typeof(PullChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.stash",
            Name = "Guardar en Stash",
            Description = "Guarda temporalmente tus cambios sin hacer commit.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "stash", "guardar temporal", "limpiar mesa", "reservar cambios" },
            TargetUseCaseType = typeof(StashChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.stash_pop",
            Name = "Recuperar de Stash",
            Description = "Recupera los cambios guardados temporalmente.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "pop", "recuperar stash", "aplicar stash", "traer de vuelta" },
            TargetUseCaseType = typeof(StashPopUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.create_branch",
            Name = "Crear Rama (Branch)",
            Description = "Crea una nueva línea de desarrollo.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "branch", "rama", "nueva rama", "crear rama" },
            TargetUseCaseType = typeof(CreateBranchUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.create_tag",
            Name = "Crear Etiqueta (Tag)",
            Description = "Marca un punto específico en el historial como importante.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "tag", "etiqueta", "versión", "marcar commit" },
            TargetUseCaseType = typeof(CreateTagUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.discard",
            Name = "Descartar Cambios",
            Description = "Limpia los cambios no deseados en tus archivos.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "descartar", "limpiar", "borrar cambios", "undo changes", "revertir archivos" },
            TargetUseCaseType = typeof(DiscardChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.undo_commit",
            Name = "Deshacer Último Commit",
            Description = "Revierte el último commit manteniendo los archivos modificados.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "undo commit", "deshacer commit", "reset soft", "corregir commit" },
            TargetUseCaseType = typeof(ResetCommitUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.switch",
            Name = "Cambiar de Rama",
            Description = "Cambia el contexto de trabajo a otra rama existente.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "switch", "checkout", "cambiar rama", "ir a la rama" },
            TargetUseCaseType = typeof(SwitchBranchUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "git.fetch",
            Name = "Fetch Remoto",
            Description = "Obtiene los últimos metadatos y commits del remoto sin fusionar.",
            Category = CapabilityCategory.Git,
            Keywords = new[] { "fetch", "traer info", "actualizar estado", "comprobar cambios" },
            TargetUseCaseType = typeof(FetchChangesUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "project.clone",
            Name = "Clonar Proyecto",
            Description = "Descarga un repositorio remoto a tu máquina local.",
            Category = CapabilityCategory.Project,
            Keywords = new[] { "clone", "clonar", "descargar repo", "bajar proyecto" },
            TargetUseCaseType = typeof(Chapi.Application.UseCases.Projects.CloneProjectUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
            Id = "project.create",
            Name = "Crear Proyecto",
            Description = "Crea un nuevo proyecto desde cero o usando una plantilla.",
            Category = CapabilityCategory.Project,
            Keywords = new[] { "crear proyecto", "nuevo proyecto", "new project", "iniciar proyecto" },
            TargetUseCaseType = typeof(Chapi.Application.UseCases.Projects.CreateProjectUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
             Id = "project.list",
             Name = "Listar Proyectos",
             Description = "Muestra una lista de los proyectos registrados en Chapi.",
             Category = CapabilityCategory.Project,
             Keywords = new[] { "listar proyectos", "ver proyectos", "mis proyectos", "cargar proyectos" },
             TargetUseCaseType = typeof(Chapi.Application.UseCases.Projects.LoadProjectsUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
             Id = "project.add",
             Name = "Agregar Proyecto Local",
             Description = "Registra una carpeta existente como proyecto en Chapi.",
             Category = CapabilityCategory.Project,
             Keywords = new[] { "agregar proyecto", "importar proyecto", "añadir carpeta" },
             TargetUseCaseType = typeof(Chapi.Application.UseCases.Projects.AddProjectUseCase)
        });

        _capabilities.Add(new AssistantCapability
        {
             Id = "project.remove",
             Name = "Eliminar Proyecto",
             Description = "Elimina un proyecto del registro de Chapi (no borra archivos).",
             Category = CapabilityCategory.Project,
             Keywords = new[] { "eliminar proyecto", "quitar proyecto", "borrar de lista", "olvidar proyecto" },
             TargetUseCaseType = typeof(Chapi.Application.UseCases.Projects.RemoveProjectUseCase)
        });
    }

    public IEnumerable<AssistantCapability> GetAllCapabilities() => _capabilities;

    public AssistantCapability? FindByIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        
        var normalizedText = text.ToLowerInvariant();
        
        // Búsqueda simple por palabras clave (esto luego se puede mejorar con IA)
        return _capabilities.OrderByDescending(c => c.Keywords.Count(k => normalizedText.Contains(k)))
                            .FirstOrDefault(c => c.Keywords.Any(k => normalizedText.Contains(k)));
    }
}
