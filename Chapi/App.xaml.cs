
using Chapi.Domain.Interfaces;
using Chapi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Velopack;
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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        private const int SW_RESTORE = 9;
        private const int WM_COPYDATA = 0x004A;

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            [MarshalAs(UnmanagedType.LPStr)]
            public string lpData;
        }

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
            services.AddSingleton<Chapi.Infrastructure.Git.LibGit2SharpRepository>();
            services.AddSingleton<Chapi.Infrastructure.Git.WslGitRepository>();
            services.AddSingleton<IGitRepository, Chapi.Infrastructure.Git.GitRepositoryDispatcher>();

            // Configuración Auth
            services.Configure<Chapi.Infrastructure.Configuration.GitAuthConfig>(Configuration.GetSection("GitAuth"));

            // Infrastructure - Auth Services
            services.AddSingleton<ICredentialStorageService, WindowsCredentialStorageService>();
            services.AddSingleton<System.Net.Http.HttpClient>();
            services.AddSingleton<Chapi.Infrastructure.Services.Auth.GitHubOAuthProvider>();
            services.AddSingleton<Chapi.Infrastructure.Services.Auth.GitLabOAuthProvider>();
            services.AddSingleton<IGitAuthProviderFactory, Chapi.Infrastructure.Services.Auth.GitAuthProviderFactory>();

            // Infrastructure - Services
            services.AddSingleton<INotificationService, MessageNotificationService>();
            services.AddSingleton<IModuleGeneratorService, ModuleGeneratorService>();
            services.AddSingleton<IGitHubAuthService, GitHubAuthService>();
            services.AddSingleton<IAssistantCapabilityRegistry, Chapi.Application.Services.Assistant.AssistantCapabilityRegistry>();
            
            // AI Services (Microsoft.Extensions.AI)
            // AI Services (Microsoft.Extensions.AI)
            services.AddTransient<Microsoft.Extensions.AI.IChatClient>(sp => 
            {
                var settings = Chapi.Infrastructure.Persistence.Settings.UserSettingsService.LoadSettings();
                
                // 1. Intentar proveedor preferido
                if (settings.PreferredAiProvider == "OpenAI" && !string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
                    return new Chapi.Infrastructure.AI.OpenAiChatClient(settings.OpenAiApiKey);
                
                if (settings.PreferredAiProvider == "Claude" && !string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
                    return new Chapi.Infrastructure.AI.ClaudeChatClient(settings.ClaudeApiKey);
                
                if ((settings.PreferredAiProvider == "Gemini" || string.IsNullOrEmpty(settings.PreferredAiProvider)) && !string.IsNullOrWhiteSpace(settings.GeminiApiKey))
                    return new Chapi.Infrastructure.AI.GeminiChatClient(settings.GeminiApiKey);

                // 2. Fallback: Probar cualquiera disponible (Prioridad: Gemini > OpenAI > Claude)
                if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
                    return new Chapi.Infrastructure.AI.GeminiChatClient(settings.GeminiApiKey);

                if (!string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
                    return new Chapi.Infrastructure.AI.OpenAiChatClient(settings.OpenAiApiKey);
                
                if (!string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
                    return new Chapi.Infrastructure.AI.ClaudeChatClient(settings.ClaudeApiKey);



                // Si llegamos aquí, no hay configuración válida
                throw new InvalidOperationException("No se ha configurado ningún proveedor de IA (Gemini, OpenAI o Claude). Por favor ve a Configuración > IA.");
            });

            // Application - Use Cases
            services.AddTransient<UseCases.CommitChangesUseCase>();
            services.AddTransient<UseCases.LoadChangesUseCase>();
            services.AddTransient<UseCases.LoadHistoryUseCase>();
            services.AddTransient<UseCases.LoadReleasesUseCase>();
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
            services.AddTransient<UseCases.DeleteTagUseCase>();
            services.AddTransient<UseCases.GetCommitStatsUseCase>();

            // Application - Project Use Cases
            services.AddTransient<Chapi.Application.UseCases.Projects.AddProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.LoadProjectsUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.RemoveProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.SwitchProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.CreateProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.UpdateProjectIndicatorsUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.CloneProjectUseCase>();
            services.AddTransient<Chapi.Application.UseCases.Projects.DeployProjectReleaseUseCase>();

            // Application - Code Generation Use Cases
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.GenerateModuleUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.GenerateModuleStructureUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApiControllerUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApiEndpointUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddApplicationMethodUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddDependencyInjectionUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddDomainMethodUseCase>();
            services.AddTransient<Chapi.Application.UseCases.CodeGeneration.AddInfrastructureMethodUseCase>();

            // Application - AI Use Cases
            services.AddTransient<Chapi.Application.UseCases.AI.GenerateCommitMessageUseCase>();
            services.AddTransient<Chapi.Application.UseCases.AI.SendChatMessageUseCase>();
            services.AddTransient<Chapi.Application.UseCases.AI.GenerateSqlQueryUseCase>();
            
            // Core Assistant Services (Singleton para mantener estado en la sesión)
            services.AddSingleton<Chapi.Application.Services.Assistant.GeminiChatService>();
            services.AddSingleton<Chapi.Application.Services.Assistant.ConversationManager>();

            // Application - Auth
            services.AddTransient<Chapi.Application.UseCases.Auth.LoginGitHubUseCase>();

            // Infrastructure - Template Service
            services.AddSingleton<ITemplateService, ProjectTemplateService>();
            services.AddSingleton<IProjectRepository, Chapi.Infrastructure.Persistence.Settings.ProjectSettingsRepository>();

            // Infrastructure - Workspace
            services.AddSingleton<Chapi.Application.Interfaces.Workspace.IWorkspaceService, Chapi.Infrastructure.Services.WorkspaceService>();

            // Presentation - ViewModels
            services.AddSingleton<Presentation.ViewModels.ChangesViewModel>();
            services.AddSingleton<Presentation.ViewModels.HistoryViewModel>();
            services.AddSingleton<Presentation.ViewModels.AssistantViewModel>();
            services.AddSingleton<Presentation.ViewModels.ReleasesViewModel>();
            services.AddSingleton<Presentation.ViewModels.WorkspaceViewModel>();
            services.AddTransient<Presentation.ViewModels.LoginGitHubViewModel>();
            services.AddTransient<Presentation.ViewModels.GitProviderSelectionViewModel>();
            services.AddSingleton<Presentation.ViewModels.CloneRepositoryViewModel>();

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
                    // Si hay argumentos (ej: abrir archivo), los enviamos vía WM_COPYDATA
                    string args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                    if (!string.IsNullOrEmpty(args))
                    {
                        byte[] s_Data = System.Text.Encoding.Default.GetBytes(args);
                        COPYDATASTRUCT cds;
                        cds.dwData = (IntPtr)100; // ID personalizado
                        cds.cbData = s_Data.Length + 1;
                        cds.lpData = args;

                        SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, ref cds);
                    }

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
            else if (msg == WM_COPYDATA)
            {
                COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
                if (cds.lpData != null)
                {
                    string args = cds.lpData;
                    // Notificamos a la ventana principal para que procese los nuevos argumentos
                    if (MainWindow is MainWindow mw)
                    {
                        mw.ProcessExternalArguments(args);
                    }
                }
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




