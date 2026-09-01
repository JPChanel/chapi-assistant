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

        private bool _showAccountsTab = true;
        public bool ShowAccountsTab
        {
            get => _showAccountsTab;
            set
            {
                _showAccountsTab = value;
                ApplyTabVisibility();
            }
        }

        private int _initialTabIndex = 0;
        private bool _isFirstLoadSelectionDone = false;

        public int SelectedTabIndex
        {
            get => ConfigTabControl?.SelectedIndex ?? _initialTabIndex;
            set
            {
                _initialTabIndex = value;
                ApplySelectedTab(value);
            }
        }

        public string DialogIdentifier { get; set; } = "RootDialog";

        public bool WasSaved { get; private set; }
        public bool SignedOut { get; private set; }

        public GitConfigDialog() : this(0)
        {
        }

        public GitConfigDialog(int initialTabIndex)
        {
            _initialTabIndex = initialTabIndex;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void ApplyTabVisibility()
        {
            if (!_showAccountsTab)
            {
                if (AccountsTabItem != null)
                {
                    AccountsTabItem.Visibility = Visibility.Collapsed;
                    AccountsTabItem.IsEnabled = false;
                }
                if (ConfigTabControl != null && AccountsTabItem != null && ConfigTabControl.Items.Contains(AccountsTabItem))
                {
                    ConfigTabControl.Items.Remove(AccountsTabItem);
                }
                if (GitTabItem != null)
                {
                    GitTabItem.IsSelected = true;
                    if (ConfigTabControl != null)
                    {
                        ConfigTabControl.SelectedItem = GitTabItem;
                    }
                }
            }
        }

        public void ApplySelectedTab(int targetIndex)
        {
            if (ConfigTabControl == null) return;

            if (!_showAccountsTab)
            {
                if (GitTabItem != null)
                {
                    GitTabItem.IsSelected = true;
                    ConfigTabControl.SelectedItem = GitTabItem;
                }
                return;
            }

            if (targetIndex == 1)
            {
                if (AccountsTabItem != null) AccountsTabItem.IsSelected = false;
                if (GitTabItem != null)
                {
                    GitTabItem.IsSelected = true;
                    ConfigTabControl.SelectedItem = GitTabItem;
                }
                ConfigTabControl.SelectedIndex = 1;
            }
            else
            {
                if (GitTabItem != null) GitTabItem.IsSelected = false;
                if (AccountsTabItem != null)
                {
                    AccountsTabItem.IsSelected = true;
                    ConfigTabControl.SelectedItem = AccountsTabItem;
                }
                ConfigTabControl.SelectedIndex = 0;
            }
        }

        private void ConfigTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_showAccountsTab)
            {
                ApplyTabVisibility();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NameTextBox?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                ApplySelectedTab(_initialTabIndex);
            }
        }

        private void ConfigTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != ConfigTabControl) return;

            if (!_showAccountsTab)
            {
                if (GitTabItem != null && !GitTabItem.IsSelected)
                {
                    GitTabItem.IsSelected = true;
                    ConfigTabControl.SelectedItem = GitTabItem;
                }
                return;
            }

            if (!_isFirstLoadSelectionDone)
            {
                if (ConfigTabControl.SelectedIndex != _initialTabIndex)
                {
                    ApplySelectedTab(_initialTabIndex);
                    return;
                }
                _isFirstLoadSelectionDone = true;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Cargar valores de Git
            NameTextBox.Text = UserName;
            EmailTextBox.Text = UserEmail;
            DefaultBranchTextBox.Text = DefaultBranch;

            // Cargar información de la cuenta
            AccountName.Text = AccountDisplayName;
            AccountUsername.Text = !string.IsNullOrWhiteSpace(AccountUserName) ? $"@{AccountUserName}" : string.Empty;
            ProviderName.Text = Provider;

            if (string.IsNullOrWhiteSpace(AccountUserName))
            {
                AccountCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                AccountCard.Visibility = Visibility.Visible;
            }

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

            ApplyTabVisibility();

            if (!_showAccountsTab)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NameTextBox?.Focus();
                    NameTextBox?.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                ApplySelectedTab(_initialTabIndex);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplySelectedTab(_initialTabIndex);
                    if (_initialTabIndex == 1)
                    {
                        NameTextBox?.Focus();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplySelectedTab(_initialTabIndex);
                    if (_initialTabIndex == 1)
                    {
                        NameTextBox?.Focus();
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void CloseDialog()
        {
            try
            {
                if (!string.IsNullOrEmpty(DialogIdentifier))
                {
                    DialogHost.Close(DialogIdentifier);
                }
                else
                {
                    DialogHost.Close(null);
                }
            }
            catch
            {
                try { DialogHost.Close(null); } catch { }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Guardar valores de Git
            UserName = NameTextBox.Text?.Trim() ?? string.Empty;
            UserEmail = EmailTextBox.Text?.Trim() ?? string.Empty;
            DefaultBranch = DefaultBranchTextBox.Text?.Trim() ?? "main";

            WasSaved = true;
            CloseDialog();
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            SignedOut = true;
            CloseDialog();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasSaved = false;
            SignedOut = false;
            CloseDialog();
        }
    }
}
