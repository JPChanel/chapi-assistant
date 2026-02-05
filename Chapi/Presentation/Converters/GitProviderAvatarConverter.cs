using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Chapi.Presentation.Converters
{
    /// <summary>
    /// Convierte username + provider + email a avatar URL (GitHub o GitLab)
    /// </summary>
    public class GitProviderAvatarConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return null;

            var username = values[0] as string;
            var provider = values[1] as Chapi.Domain.Enums.GitProvider?;
            
            if (string.IsNullOrWhiteSpace(username) || provider == null)
            {
                return CreateDefaultAvatar();
            }

            var size = int.TryParse(parameter?.ToString(), out var parsedSize) ? parsedSize : 80;
            string url;

            var avatarCache = Chapi.Domain.Services.AvatarCacheService.Instance;

            switch (provider)
            {
                case Chapi.Domain.Enums.GitProvider.GitHub:
                    url = avatarCache.GetGitHubAvatarUrl(username, size);
                    break;

                case Chapi.Domain.Enums.GitProvider.GitLab:
                    url = avatarCache.GetGitLabAvatarUrl(username, size);
                    break;

                default:
                    return CreateDefaultAvatar();
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                
                bitmap.DownloadFailed += (s, e) =>
                {
                };
                
                bitmap.EndInit();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private BitmapImage CreateDefaultAvatar()
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri("https://www.gravatar.com/avatar/00000000000000000000000000000000?d=mp&s=80", UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}



