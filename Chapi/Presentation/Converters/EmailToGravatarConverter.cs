using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Converters
{
    /// <summary>
    /// Convierte un email a una URL de Gravatar
    /// </summary>
    public class EmailToGravatarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string url;

            if (value is not string email || string.IsNullOrWhiteSpace(email))
            {
                // Retorna imagen por defecto si no hay email
                url = "https://www.gravatar.com/avatar/00000000000000000000000000000000?d=mp&s=80";
            }
            else
            {
                var hash = GetMd5Hash(email.Trim().ToLowerInvariant());
                var size = parameter?.ToString() ?? "80";

                // d=mp: usa el avatar "mystery person" por defecto
                // s=size: tamaño de la imagen
                url = $"https://www.gravatar.com/avatar/{hash}?d=mp&s={size}";
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();

                return bitmap;
            }
            catch (Exception ex)
            {

                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static string GetMd5Hash(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}

