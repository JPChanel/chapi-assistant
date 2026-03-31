namespace Chapi.Presentation.Alerts.Service;

public static class AppServices
{
    public static IAlertService AlertService { get; private set; } = null!;


    public static bool IsConfigured { get; private set; }

    public static void Configure(IAlertService alertService)
    {
        AlertService = alertService;
        IsConfigured = true;
    }
}
