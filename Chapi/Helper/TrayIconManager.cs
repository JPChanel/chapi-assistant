using Chapi.Model;
using Hardcodet.Wpf.TaskbarNotification;
using MaterialDesignThemes.Wpf;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Chapi
{
    public class TrayIconManager
    {
        private TaskbarIcon taskbarIcon;
        private MainWindow mainWindow;
        private MenuItem _projectMenuItem;
        private MenuItem _projectsSubMenu;
        public TrayIconManager(MainWindow window)
        {
            mainWindow = window;
            taskbarIcon = new TaskbarIcon
            {
                Icon = new System.Drawing.Icon("Image/icon.ico"),
                ToolTipText = "Chapi Asistente 🚀",
                ContextMenu = CreateContextMenu()
            };

            // Evento doble clic
            taskbarIcon.TrayMouseDoubleClick += (s, e) =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            };
        }

        private ContextMenu CreateContextMenu()
        {

            var contextMenu = new ContextMenu();

            contextMenu.Items.Add(CreateMenuItem("Crear Nuevo Proyecto", PackIconKind.FolderPlusOutline, (s, e) => { Msg.User("Crear Nuevo Proyecto"); mainWindow.CreateNewTemplate(); }));
 
            _projectsSubMenu = CreateMenuItem("Cambiar a Proyecto", PackIconKind.SwapHorizontal, null);
            _projectsSubMenu.Items.Add(CreateMenuItem(
                "Agregar Proyecto Existente...",
                PackIconKind.FolderAdd,
                (s, e) => {
                    Msg.User("Asociar Proyecto Existente");
                    mainWindow.SelectProjectMenu_Click(s, e);
                }
            ));
            _projectsSubMenu.Items.Add(new Separator());
            contextMenu.Items.Add(_projectsSubMenu);

            // Dynamic project menu item
            _projectMenuItem = CreateMenuItem("Proyecto no asociado", PackIconKind.Sitemap, null);
            _projectMenuItem.IsEnabled = false;
            contextMenu.Items.Add(_projectMenuItem);

            contextMenu.Items.Add(new Separator());

            contextMenu.Items.Add(CreateMenuItem("Generar Estructura Base", PackIconKind.CodeBraces, (s, e) => mainWindow.GenerateModuleMenu_Click()));
            contextMenu.Items.Add(CreateMenuItem("Asociar Git", PackIconKind.Git, (s, e) => mainWindow.AsociateGitMenu_Click()));
            contextMenu.Items.Add(CreateMenuItem("Agregar Método", PackIconKind.SourceFork, (s, e) => mainWindow.AddMethod_Click()));
            contextMenu.Items.Add(CreateMenuItem("Gestionar Rollbacks", PackIconKind.ReceiptRoll, (s, e) => mainWindow.RollbackSelectModule()));
            contextMenu.Items.Add(CreateMenuItem("Ver Log de Procesos", PackIconKind.History, (s, e) => mainWindow.AddClassLog_Click()));

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateMenuItem("Servicios", PackIconKind.InformationOutline, (s, e) => {
                mainWindow.ShowUpdateView();
            }));
            contextMenu.Items.Add(CreateMenuItem("Salir", PackIconKind.Logout, (s, e) => Application.Current.Shutdown()));

            return contextMenu;


        }
        private MenuItem CreateMenuItem(string header, PackIconKind iconKind, RoutedEventHandler clickHandler)
        {
            var menuItem = new MenuItem { Header = header };
            menuItem.Icon = new PackIcon { Kind = iconKind, Width = 16, Height = 16 };
            if (clickHandler != null)
            {
                menuItem.Click += clickHandler;
            }
            return menuItem;
        }
        public void ShowNotification(string title, string message)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
            }
            else
            {
                dispatcher.Invoke(() =>
                {
                    taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
                });
            }
        }
        public void UpdateProjectMenuItem(string nombreProyecto, bool habilitado = true)
        {
            if (_projectMenuItem != null)
            {
                _projectMenuItem.Header = $"Proyecto: {nombreProyecto}";
                _projectMenuItem.IsEnabled = habilitado;
            }
        }
        /// <summary>
        /// Es llamado por MainWindow para poblar la lista de proyectos en el TrayIcon.
        /// </summary>
        public void UpdateProjectList(List<ProjectViewModel> projects) 
        {
            while (_projectsSubMenu.Items.Count > 2)
            {
                _projectsSubMenu.Items.RemoveAt(2);
            }

            // 2. Añade los nuevos proyectos
            foreach (var proj in projects)
            {
                var projectItem = new MenuItem
                {
                    Header = proj.Name,
                    CommandParameter = proj.FullPath, 
                    Icon = new PackIcon { Kind = proj.Icon, Width = 16, Height = 16 }
                };

                // 3. Asigna el evento de clic
                projectItem.Click += ProjectItem_Click;
                _projectsSubMenu.Items.Add(projectItem);
            }
        }
        /// <summary>
        /// Se dispara cuando el usuario hace clic en un proyecto del submenú.
        /// </summary>
        private void ProjectItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            string path = menuItem.CommandParameter as string;

            if (string.IsNullOrEmpty(path)) return;

            // Llama al nuevo método en MainWindow
            mainWindow.SwitchToProject(path);
        }
    }
}
