namespace Chapi.Application.Interfaces;

public interface IUsageTelemetryService
{
    Task TrackAppOpenAsync();
    Task FlushPendingAsync();
}
