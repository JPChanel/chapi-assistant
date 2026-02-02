
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Velopack;
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Git;
using Chapi.Infrastructure.Services;
using UseCases = Chapi.Application.UseCases.Git;



namespace Chapi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int SW_RESTORE = 9; // Constante para restaurar una ventana
        private static uint _restoreMessage;
        public static string GlobalDialogIdentifier => "RootDialog";
        public static TrayIconManager TrayIconManager { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        public static NetworkWatcherService NetworkWatcher { get; private set; }
        
        // Dependency Injection
        public static IServiceProvider ServiceProvider { get; private set; }

        private static void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Infrastructure - Git
            services.AddSingleton<GitCommandExecutor>();
            services.AddSingleton<GitOutputParser>();
            services.AddSingleton<IGitRepository, GitRepository>();

            // Infrastructure - Services
            services.AddSingleton<INotificationService, MessageNotificationService>();
            services.AddSingleton<ModuleGeneratorService>();

            // Application - Use Cases
            services.AddTransient<UseCases.CommitChangesUseCase>();
            services.AddTransient<UseCases.LoadChangesUseCase>();
            services.AddTransient<UseCases.LoadHistoryUseCase>();
            services.AddTransient<UseCases.PushChangesUseCase>();
            services.AddTransient<UseCases.PullChangesUseCase>();
            services.AddTransient<UseCases.FetchChangesUseCase>();
            services.AddTransient<UseCases.SwitchBranchUseCase>();
            services.AddTransient<UseCases.GetBranchesUseCase>();
            services.AddTransient<UseCases.StashChangesUseCase>();
            services.AddTransient<UseCases.StashPopUseCase>();
            services.AddTransient<UseCases.StashClearUseCase>();
            services.AddTransient<UseCases.StashDropUseCase>();
            services.AddTransient<UseCases.DiscardChangesUseCase>();
            services.AddTransient<UseCases.ResetCommitUseCase>();
            services.AddTransient<UseCases.CreateBranchUseCase>();
            services.AddTransient<UseCases.CreateTagUseCase>();
            services.AddTransient<UseCases.GetFilesChangedInCommitUseCase>();
            services.AddTransient<UseCases.GetFileDiffUseCase>();
            services.AddTransient<UseCases.AssociateGitUseCase>();

            // Application - Project Use Cases
            services.AddTransient<Chapi.Application.UseCases.Projects.AddProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.LoadProjectsUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.RemoveProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.SwitchProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.CreateProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.UpdateProjectIndicatorsUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.CloneProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.GenerateModuleUseCase>();

            // Infrastructure - Template Service
            services.AddSingleton<ITemplateService, ProjectTemplateService>();
            services.AddSingleton<IProjectRepository, Chapi.Infrastructure.Persistence.Settings.ProjectSettingsRepository>();

            // Presentation - ViewModels
            services.AddTransient<Presentation.ViewModels.ChangesViewModel>();
            services.AddTransient<Presentation.ViewModels.HistoryViewModel>();
            services.AddTransient<Presentation.ViewModels.AssistantViewModel>();

            ServiceProvider = services.BuildServiceProvider();
        }


        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build().Run();
            App app = new();
            app.InitializeComponent();
            app.Run();
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            _restoreMessage = RegisterWindowMessage("CHAPI_RESTORE_WINDOW_MSG");
            _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                var currentProcess = Process.GetCurrentProcess();
                var otherProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                    .FirstOrDefault(p => p.Id != currentProcess.Id);

                IntPtr hWnd = IntPtr.Zero;
                if (otherProcess != null)
                {
                    hWnd = otherProcess.MainWindowHandle;
                }

                if (hWnd == IntPtr.Zero)
                {
                    hWnd = FindWindow(null, "Chapi Assistance");
                }

                if (hWnd != IntPtr.Zero)
                {
                    // Enviamos el mensaje para que la otra instancia se "levante" sola
                    PostMessage(hWnd, _restoreMessage, IntPtr.Zero, IntPtr.Zero);
                }

                Shutdown();
                return;
            }
            base.OnStartup(e);
            var builder = new ConfigurationBuilder()
               .SetBasePath(AppContext.BaseDirectory)
               .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
            
            // Configurar Dependency Injection
            ConfigureServices();

            // Init NetworkWatcher with DI
            var gitRepo = ServiceProvider.GetRequiredService<IGitRepository>();
            NetworkWatcher = new NetworkWatcherService(gitRepo);
            
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MainWindow = new MainWindow();
            
            // Hook para escuchar el mensaje de restauracion
            MainWindow.Loaded += (s, ev) =>
            {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle);
                source.AddHook(HandleMessages);
            };

            TrayIconManager = new TrayIconManager((MainWindow)MainWindow);
            MainWindow.Show();
            ConfigureExceptionHandling();
        }

        private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _restoreMessage)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
                handled = true;
            }
            return IntPtr.Zero;
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
                await DialogService.ShowConfirmDialog("Error", ex.Message, Chapi.Presentation.Views.Dialogs.DialogVariant.Error, Chapi.Presentation.Views.Dialogs.DialogType.Info);

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




