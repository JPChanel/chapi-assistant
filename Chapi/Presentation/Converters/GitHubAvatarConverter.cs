using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Converters
{
    /// <summary>
    /// Convierte un username de GitHub a su avatar
    /// </summary>
    public class GitHubAvatarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string url;
            
            if (value is not string username || string.IsNullOrWhiteSpace(username))
            {
                // Retorna imagen por defecto si no hay username
                url = "https://avatars.githubusercontent.com/u/0?v=4&s=80";
                System.Diagnostics.Debug.WriteLine($"⚠️ GitHubAvatarConverter: Username vacío, usando imagen por defecto");
            }
            else
            {
                var size = parameter?.ToString() ?? "80";
                
                // GitHub avatar URL
                url = $"https://avatars.githubusercontent.com/{username}?v=4&s={size}";
                System.Diagnostics.Debug.WriteLine($"✅ GitHubAvatarConverter: Username '{username}' -> URL: {url}");
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                
                // Evento para detectar cuando se descarga
                bitmap.DownloadCompleted += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"✅ GitHubAvatarConverter: Imagen descargada exitosamente");
                };
                
                bitmap.DownloadFailed += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"❌ GitHubAvatarConverter: Error descargando imagen: {e.ErrorException?.Message}");
                };
                
                bitmap.EndInit();
                
                System.Diagnostics.Debug.WriteLine($"🔄 GitHubAvatarConverter: Iniciando descarga asíncrona...");
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GitHubAvatarConverter: Error cargando imagen: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
