using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Persistence.Settings;
using Chapi.Infrastructure.Services;
using Chapi.Presentation.Shared.Notifications.Services;
using Chapi.Startup;
using Chapi.Startup.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Velopack;

namespace Chapi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string AppMutexName = "ChapiAssistan-7E8F4A2B-1D6C-4B8A-9A8C-5D6B7E9F0A3D";
        private const string AppSettingsFileName = "appsettings.json";
        private const string RestoreWindowMessageName = "CHAPI_RESTORE_WINDOW_MSG";
        private const string MainWindowTitle = "Chapi Assistance";

        private SingleInstanceManager? _singleInstanceManager;
        private ExceptionHandling? _exceptionHandling;

        public static string GlobalDialogIdentifier => "RootDialog";
        public static TrayIconManager TrayIconManager { get; private set; } = null!;
        public static IConfiguration Configuration { get; private set; } = null!;
        public static NetworkWatcherService NetworkWatcher { get; private set; } = null!;
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceManager = new SingleInstanceManager(
                AppMutexName,
                MainWindowTitle,
                RestoreWindowMessageName);

            if (_singleInstanceManager.TryRedirectToExistingInstance(Environment.GetCommandLineArgs().Skip(1).ToArray()))
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            var uiSettings = UserSettingsService.LoadSettings();
            ThemeService.ApplyTheme(uiSettings.ThemeMode);

            Configuration = AppConfigurationLoader.Load(AppSettingsFileName);
            ServiceProvider = new ServiceCollection()
                .AddChapiServices(Configuration)
                .BuildServiceProvider();

            AppServices.Configure(ServiceProvider.GetRequiredService<IAlertService>());

            var gitRepo = ServiceProvider.GetRequiredService<IGitRepository>();
            NetworkWatcher = new NetworkWatcherService(gitRepo);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            _singleInstanceManager.AttachToWindow(mainWindow, mainWindow.ProcessExternalArguments);

            TrayIconManager = new TrayIconManager(mainWindow);

            _exceptionHandling = new ExceptionHandling(this);
            _exceptionHandling.Register();

            mainWindow.Show();
        }

        public static void ReleaseMutex()
        {
            if (Current is App app)
            {
                app._singleInstanceManager?.Release();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TrayIconManager?.Dispose();
            NetworkWatcher?.Dispose();
            _exceptionHandling?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }
    }
}
