namespace Chapi.Domain.Entities;

public record GitStash(string Name, string Branch, string Message, int FileCount = 0);
