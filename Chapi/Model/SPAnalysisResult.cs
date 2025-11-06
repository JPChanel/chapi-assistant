namespace Chapi.Model;

public class SPAnalysisResult
{
    public string StoredProcedureName { get; set; }
    public List<string> RequestParameters { get; set; } = new();
    public List<string> Parameters { get; set; } = new();
    public List<string> DTOFields { get; set; } = new();
    public List<string> ResponseMapper { get; set; } = new();
}
