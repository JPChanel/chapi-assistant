using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Chapi.Presentation.Shared.Dialogs.Views
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
        public string PackageId => PackageIdBox.Text;
        public string Author => AuthorBox.Text;
        public string Platform => (PlatformBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Sin especificar"
            ? string.Empty
            : (PlatformBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        public string IconPath => IconPathBox.Text;
        public string SplashPath => SplashPathBox.Text;

        public bool IsFolderTarget => RadioDestFolder.IsChecked == true;
        public string LocalPath => LocalPathBox.Text;
        
        public string FtpUrl => FtpUrlBox.Text;
        public string FtpUser => FtpUserBox.Text;
        public string FtpPassword => FtpPassBox.Password;

        // Método para cargar datos iniciales
        public void SetDefaults(string appName, string packageId, string author, string localPath, string ftpUrl, string ftpUser, string platform = "", string iconPath = "", string splashPath = "")
        {
            AppNameBox.Text = appName;
            PackageIdBox.Text = packageId;
            AuthorBox.Text = author;
            SelectPlatform(platform);
            IconPathBox.Text = iconPath;
            SplashPathBox.Text = splashPath;
            
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

            UpdatePreviews();
        }

        private void SelectPlatform(string platform)
        {
            foreach (var item in PlatformBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content?.ToString(), platform, StringComparison.OrdinalIgnoreCase))
                {
                    PlatformBox.SelectedItem = comboItem;
                    return;
                }
            }

            PlatformBox.SelectedIndex = 0;
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

        private void SelectIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Iconos (*.ico)|*.ico|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar Icono de Aplicación"
            };

            if (dialog.ShowDialog() == true)
            {
                IconPathBox.Text = dialog.FileName;
                UpdatePreviews();
            }
        }

        private void SelectSplash_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar Imagen Splash"
            };

            if (dialog.ShowDialog() == true)
            {
                SplashPathBox.Text = dialog.FileName;
                UpdatePreviews();
            }
        }

        private void ImagePathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreviews();
        }

        private void UpdatePreviews()
        {
            try
            {
                IconPreview.Source = LoadImage(IconPathBox.Text);
                SplashPreview.Source = LoadImage(SplashPathBox.Text);
            }
            catch { }
        }

        private BitmapImage? LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Evita bloqueo de archivo
                bitmap.DecodePixelWidth = 32; // Optimizar para miniatura
                bitmap.EndInit();
                return bitmap;
            }
            catch { return null; }
        }
    }
}
