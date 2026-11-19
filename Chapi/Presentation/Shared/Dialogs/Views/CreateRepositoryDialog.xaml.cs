using MaterialDesignThemes.Wpf;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using DialogResult = System.Windows.Forms.DialogResult;

namespace Chapi.Presentation.Shared.Dialogs.Views
{
    public partial class CreateRepositoryDialog : UserControl
    {
        private bool _isSettingDefaults;
        private bool _isTemplateMode;

        public bool IsConfirmed { get; private set; }
        public string ProjectPath { get; private set; } = string.Empty;
        public string ParentDirectory { get; private set; } = string.Empty;
        public string ProjectName { get; private set; } = string.Empty;
        public string DefaultBranch { get; private set; } = "main";
        public string? RemoteUrl { get; private set; }
        public bool CreateReadme { get; private set; }
        public bool CreateGitIgnore { get; private set; }

        public CreateRepositoryDialog()
        {
            InitializeComponent();
        }

        public void SetDefaults(string? initialPath = null, string? defaultBranch = null)
        {
            _isSettingDefaults = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(defaultBranch))
                {
                    DefaultBranchTextBox.Text = defaultBranch.Trim();
                }

                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    LocalPathTextBox.Text = initialPath.Trim();
                    ProjectNameTextBox.Text = new DirectoryInfo(initialPath).Name;
                }
                else
                {
                    var defaultReposDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
                    LocalPathTextBox.Text = defaultReposDir;
                }
            }
            finally
            {
                _isSettingDefaults = false;
            }
        }

        public void ConfigureForTemplateProject()
        {
            _isTemplateMode = true;
            DialogTitleTextBlock.Text = "Crear Nuevo Proyecto";
            CreateButton.Content = "CREAR PROYECTO";
            DefaultBranchContainer.Visibility = Visibility.Collapsed;
            InitOptionsBorder.Visibility = Visibility.Collapsed;
            SetDefaults(null, null);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPath = LocalPathTextBox.Text?.Trim();
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = !string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath)
                    ? currentPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LocalPathTextBox.Text = dialog.SelectedPath;
                if (!_isTemplateMode && string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
                {
                    ProjectNameTextBox.Text = new DirectoryInfo(dialog.SelectedPath).Name;
                }
            }
        }

        private void LocalPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSettingDefaults || _isTemplateMode) return;

            if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) && !string.IsNullOrWhiteSpace(LocalPathTextBox.Text))
            {
                try
                {
                    ProjectNameTextBox.Text = new DirectoryInfo(LocalPathTextBox.Text).Name;
                }
                catch { }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogHost.Close(App.GlobalDialogIdentifier, false);
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var path = LocalPathTextBox.Text?.Trim() ?? string.Empty;
            var name = ProjectNameTextBox.Text?.Trim() ?? string.Empty;
            var branch = DefaultBranchTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                System.Windows.MessageBox.Show("Por favor especifica una ruta válida para el proyecto.", "Ruta requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isTemplateMode && string.IsNullOrWhiteSpace(name))
            {
                System.Windows.MessageBox.Show("Por favor especifica un nombre para el proyecto.", "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetPath = path;
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!string.IsNullOrWhiteSpace(name) && !string.Equals(dirInfo.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = Path.Combine(path, name);
                }
            }
            catch { }

            ParentDirectory = path;
            ProjectName = name;
            ProjectPath = targetPath;
            DefaultBranch = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
            RemoteUrl = string.IsNullOrWhiteSpace(RemoteUrlTextBox.Text) ? null : RemoteUrlTextBox.Text.Trim();
            CreateReadme = CreateReadmeCheckBox.IsChecked == true;
            CreateGitIgnore = CreateGitIgnoreCheckBox.IsChecked == true;
            IsConfirmed = true;

            DialogHost.Close(App.GlobalDialogIdentifier, true);
        }
    }
}
