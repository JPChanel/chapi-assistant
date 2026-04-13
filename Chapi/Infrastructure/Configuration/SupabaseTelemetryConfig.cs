namespace Chapi.Infrastructure.Configuration;

public sealed class SupabaseTelemetryConfig
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = string.Empty;
    public string AnonKey { get; set; } = string.Empty;
    public string TableName { get; set; } = "app_usage";
}
