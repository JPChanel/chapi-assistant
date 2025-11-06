using Chapi.Services;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Velopack;

namespace Chapi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string AppMutexName = "ChapiAssistan-7E8F4A2B-1D6C-4B8A-9A8C-5D6B7E9F0A3D";
        private static Mutex _mutex;

        // 2. Importamos las funciones de Windows API para "despertar" la ventana
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9; // Constante para restaurar una ventana
        public static string GlobalDialogIdentifier => "RootDialog";
        public static TrayIconManager TrayIconManager { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        public static NetworkWatcherService NetworkWatcher { get; private set; }
        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            NetworkWatcher = new NetworkWatcherService();
            App app = new();
            app.InitializeComponent();
            app.Run();
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                var currentProcess = Process.GetCurrentProcess();
                var otherProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                    .FirstOrDefault(p => p.Id != currentProcess.Id);

                if (otherProcess != null)
                {
                    // Encontramos la otra instancia. ¡Hay que mostrarla!
                    IntPtr hWnd = otherProcess.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        // 4. Usamos Windows API para restaurarla y traerla al frente
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                }

                using (var icon = new System.Windows.Forms.NotifyIcon())
                {
                    icon.Icon = System.Drawing.SystemIcons.Information;
                    icon.Visible = true;
                    icon.BalloonTipTitle = "Ninja";
                    icon.BalloonTipText = "👋 ¡Hey! Crack Soy Chapi 🤖, La aplicación ya se encuentra abierta en el área de notificación.";
                    icon.ShowBalloonTip(3000);
                }
                Shutdown();
                return;
            }
            base.OnStartup(e);
            var builder = new ConfigurationBuilder()
               .SetBasePath(AppContext.BaseDirectory)
               .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MainWindow = new MainWindow();
            TrayIconManager = new TrayIconManager((MainWindow)MainWindow);
            MainWindow.Show();
            ConfigureExceptionHandling();
        }

        private void ConfigureExceptionHandling()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            ShowAlert(e.Exception);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowAlert(e.ExceptionObject as Exception);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            ShowAlert(e.Exception);
            e.SetObserved();
        }

        private void ShowAlert(Exception ex)
        {
            if (ex == null) return;
            Current.Dispatcher.Invoke(async () =>
            {
                await DialogService.ShowConfirmDialog("Error", ex.Message, Views.Dialogs.DialogVariant.Error, Views.Dialogs.DialogType.Info);

            });
        }
        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
