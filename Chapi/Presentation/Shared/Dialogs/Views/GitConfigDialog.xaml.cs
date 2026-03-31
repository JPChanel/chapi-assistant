using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Shared.Dialogs.Views
{
    public partial class GitConfigDialog : UserControl
    {
        // Git configuration
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string DefaultBranch { get; set; } = "main";

        // Account information
        public string AccountDisplayName { get; set; } = string.Empty;
        public string AccountUserName { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public BitmapImage AvatarImage { get; set; }

        public bool WasSaved { get; private set; }
        public bool SignedOut { get; private set; }

        public GitConfigDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Cargar valores de Git
            NameTextBox.Text = UserName;
            EmailTextBox.Text = UserEmail;
            DefaultBranchTextBox.Text = DefaultBranch;

            // Cargar información de la cuenta
            AccountName.Text = AccountDisplayName;
            AccountUsername.Text = $"@{AccountUserName}";
            ProviderName.Text = Provider;

            // Configurar icono del provider
            if (Provider == "GitHub")
            {
                ProviderIcon.Kind = PackIconKind.Github;
                ProviderIcon.Foreground = System.Windows.Media.Brushes.White;
            }
            else if (Provider == "GitLab")
            {
                ProviderIcon.Kind = PackIconKind.Gitlab;
                ProviderIcon.Foreground = System.Windows.Media.Brushes.Orange;
            }

            // Cargar avatar
            if (AvatarImage != null)
            {
                AccountAvatar.Source = AvatarImage;
            }

            // Focus en el primer campo
            NameTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Guardar valores de Git
            UserName = NameTextBox.Text?.Trim() ?? string.Empty;
            UserEmail = EmailTextBox.Text?.Trim() ?? string.Empty;
            DefaultBranch = DefaultBranchTextBox.Text?.Trim() ?? "main";

            WasSaved = true;
            DialogHost.Close("RootDialog");
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            SignedOut = true;
            DialogHost.Close("RootDialog");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasSaved = false;
            SignedOut = false;
            DialogHost.Close("RootDialog");
        }
    }
}
