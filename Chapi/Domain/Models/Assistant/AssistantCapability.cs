using System;

namespace Chapi.Domain.Models.Assistant;

public enum CapabilityCategory
{
    Git,
    Navigation,
    AI,
    System,
    Project
}

public class AssistantCapability
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CapabilityCategory Category { get; set; }
    public string[] Keywords { get; set; } = Array.Empty<string>();
    
    // Indica qué UseCase o Comando debe activarse
    public Type TargetUseCaseType { get; set; } = null!;
}
