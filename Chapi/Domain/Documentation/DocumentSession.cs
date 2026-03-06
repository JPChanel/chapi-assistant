namespace Chapi.Domain.Documentation;

public class DocumentSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public DocTemplate Template { get; set; } = DocTemplate.ModeloSoftware;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastModifiedAt { get; set; } = DateTime.Now;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<DocSection> Sections { get; set; } = new();
}
