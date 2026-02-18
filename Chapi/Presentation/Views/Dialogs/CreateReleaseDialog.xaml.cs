using System.Windows.Controls;

namespace Chapi.Presentation.Views.Dialogs
{
    public partial class CreateReleaseDialog : UserControl
    {
        public CreateReleaseDialog()
        {
            InitializeComponent();
        }

        public string TagName => TagNameBox.Text;
        public string Message => MessageBox.Text;
        public bool IsRemote => RadioRemote.IsChecked == true;
        public bool IsLocal => RadioLocal.IsChecked == true;

        // Nuevos campos para Build Config
        // Nuevos campos para Configuración de Destino
        public string AppName => AppNameBox.Text;
        public string Author => AuthorBox.Text;

        public bool IsFolderTarget => RadioDestFolder.IsChecked == true;
        public string LocalPath => LocalPathBox.Text;
        
        public string FtpUrl => FtpUrlBox.Text;
        public string FtpUser => FtpUserBox.Text;
        public string FtpPassword => FtpPassBox.Password;

        // Método para cargar datos iniciales
        public void SetDefaults(string appName, string author, string localPath, string ftpUrl, string ftpUser)
        {
            AppNameBox.Text = appName;
            AuthorBox.Text = author;
            
            // Prioridad: Si hay FTP URL, activar FTP, si no, Carpeta
            if (!string.IsNullOrEmpty(ftpUrl))
            {
                RadioDestFtp.IsChecked = true;
                FtpUrlBox.Text = ftpUrl;
                FtpUserBox.Text = ftpUser;
            }
            else
            {
                RadioDestFolder.IsChecked = true;
                LocalPathBox.Text = localPath;
            }
        }

        private void TagNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Autocompletado del mensaje si está vacío o es por defecto
            string tag = TagNameBox.Text;
            if (string.IsNullOrWhiteSpace(MessageBox.Text) || MessageBox.Text.StartsWith("Release "))
            {
                MessageBox.Text = !string.IsNullOrWhiteSpace(tag) ? $"Release {tag}" : string.Empty;
            }
        }
    }
}
