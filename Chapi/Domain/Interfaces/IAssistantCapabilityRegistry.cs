using Chapi.Domain.Models.Assistant;
using System.Collections.Generic;

namespace Chapi.Domain.Interfaces;

public interface IAssistantCapabilityRegistry
{
    IEnumerable<AssistantCapability> GetAllCapabilities();
    AssistantCapability? FindByIntent(string text);
}
