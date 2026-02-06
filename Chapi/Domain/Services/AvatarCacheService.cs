using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chapi.Domain.Interfaces;

namespace Chapi.Domain.Services
{
    /// <summary>
    /// Servicio singleton para cachear avatares de usuarios autenticados
    /// Evita llamadas repetidas a APIs cuando se cambia entre proyectos
    /// </summary>
    public class AvatarCacheService
    {
        private static readonly Lazy<AvatarCacheService> _instance = new(() => new AvatarCacheService());
        public static AvatarCacheService Instance => _instance.Value;

        private readonly Dictionary<string, string> _avatarCache = new();
        private readonly HashSet<string> _pendingRequests = new();
        private readonly object _cacheLock = new();
        private readonly string _localAvatarsPath;

        public event EventHandler<AvatarUpdatedEventArgs> AvatarUpdated;

        private AvatarCacheService()
        {
            _localAvatarsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Chapi", "Avatars");
            
            if (!Directory.Exists(_localAvatarsPath))
            {
                Directory.CreateDirectory(_localAvatarsPath);
            }
        }

        private async Task<string> EnsureLocalAvatarAsync(string provider, string username, string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl) || remoteUrl.Contains("gravatar.com"))
                return remoteUrl;

            var extension = ".png"; // Por defecto
            if (remoteUrl.Contains(".jpg") || remoteUrl.Contains(".jpeg")) extension = ".jpg";
            
            var fileName = $"{provider}_{username}{extension}";
            var localPath = Path.Combine(_localAvatarsPath, fileName);

            if (File.Exists(localPath))
            {
                return new Uri(localPath).AbsoluteUri;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ChapiAssistant");
                var data = await client.GetByteArrayAsync(remoteUrl);
                await File.WriteAllBytesAsync(localPath, data);
                return new Uri(localPath).AbsoluteUri;
            }
            catch
            {
                return remoteUrl;
            }
        }

        protected virtual void OnAvatarUpdated(string provider, string username)
        {
            AvatarUpdated?.Invoke(this, new AvatarUpdatedEventArgs(provider, username));
        }

        /// <summary>
        /// Obtiene el avatar URL para un usuario de GitHub
        /// </summary>
        public string GetGitHubAvatarUrl(string username, int size = 80)
        {
            if (string.IsNullOrWhiteSpace(username))
                return GetDefaultAvatarUrl(size);

            var cacheKey = $"github:{username}:{size}";
            
            lock (_cacheLock)
            {
                if (_avatarCache.TryGetValue(cacheKey, out var cachedUrl))
                {
                    return cachedUrl;
                }
            }

            // Verificar si existe localmente
            var localFile = Path.Combine(_localAvatarsPath, $"GitHub_{username}.png");
            if (File.Exists(localFile))
            {
                var localUrl = new Uri(localFile).AbsoluteUri;
                lock (_cacheLock) { _avatarCache[cacheKey] = localUrl; }
                return localUrl;
            }

            var remoteUrl = $"https://avatars.githubusercontent.com/{username}?v=4&s={size}";
            
            // Descargar en background para la prÃ³xima vez
            _ = Task.Run(async () => {
                var local = await EnsureLocalAvatarAsync("GitHub", username, remoteUrl);
                if (local.StartsWith("file:"))
                {
                    lock (_cacheLock) { _avatarCache[cacheKey] = local; }
                    OnAvatarUpdated("GitHub", username);
                }
            });

            return remoteUrl;
        }

        /// <summary>
        /// Obtiene el avatar URL para un usuario de GitLab (consulta API si es necesario)
        /// </summary>
        public async Task<string> GetGitLabAvatarUrlAsync(string username, int size = 80)
        {
            if (string.IsNullOrWhiteSpace(username))
                return GetDefaultAvatarUrl(size);

            var cacheKey = $"gitlab:{username}:{size}";
            
            lock (_cacheLock)
            {
                if (_avatarCache.TryGetValue(cacheKey, out var cachedUrl))
                {
                    return cachedUrl;
                }
            }

            // Consultar API de GitLab
            try
            {
                var apiUrl = $"https://gitlab.com/api/v4/users?username={Uri.EscapeDataString(username)}";
                System.Diagnostics.Debug.WriteLine($"🔍 AvatarCache: GitLab API - Consultando @{username}");
                
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ChapiAssistant");
                var response = await client.GetStringAsync(apiUrl);
                
                var avatarUrlMatch = Regex.Match(response, "\"avatar_url\":\"([^\"]+)\"");
                if (avatarUrlMatch.Success)
                {
                    var remoteUrl = avatarUrlMatch.Groups[1].Value;
                    
                    // Asegurar localmente
                    var localUrl = await EnsureLocalAvatarAsync("GitLab", username, remoteUrl);
                    
                    lock (_cacheLock)
                    {
                        _avatarCache[cacheKey] = localUrl;
                    }
                    
                    return localUrl;
                }
            }
            catch (Exception ex)
            {
            }

            // Fallback
            var fallbackUrl = GetDefaultAvatarUrl(size);
            return fallbackUrl;
        }

        /// <summary>
        /// Versión sincrónica para GitLab (usa Task.Run para no bloquear UI)
        /// </summary>
        public string GetGitLabAvatarUrl(string username, int size = 80)
        {
            if (string.IsNullOrWhiteSpace(username))
                return GetDefaultAvatarUrl(size);

            var cacheKey = $"gitlab:{username}:{size}";
            
            // Verificar caché primero (sin bloquear)
            lock (_cacheLock)
            {
                if (_avatarCache.TryGetValue(cacheKey, out var cachedUrl))
                {
                    return cachedUrl;
                }

                // Si ya hay una consulta en progreso para este usuario, retornar temporal
                if (_pendingRequests.Contains(cacheKey))
                {
                    return GetDefaultAvatarUrl(size);
                }

                // Marcar como en progreso
                _pendingRequests.Add(cacheKey);
            }

            // Si no está en caché, retornar URL temporal y consultar en background
            var tempUrl = GetDefaultAvatarUrl(size);
            
            // Consultar API en background sin bloquear
            Task.Run(async () =>
            {
                try
                {
                    var apiUrl = $"https://gitlab.com/api/v4/users?username={Uri.EscapeDataString(username)}";
                    System.Diagnostics.Debug.WriteLine($"🔍 AvatarCache: GitLab API - Consultando @{username} (background)");
                    
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "ChapiAssistant");
                    client.Timeout = TimeSpan.FromSeconds(5); // Timeout de 5 segundos
                    var response = await client.GetStringAsync(apiUrl);
                    
                    var avatarUrlMatch = Regex.Match(response, "\"avatar_url\":\"([^\"]+)\"");
                    if (avatarUrlMatch.Success)
                    {
                        var url = avatarUrlMatch.Groups[1].Value;
                        
                        lock (_cacheLock)
                        {
                            _avatarCache[cacheKey] = url;
                            _pendingRequests.Remove(cacheKey); // Quitar de pending
                        }
                        // Notificar que el avatar se actualizó
                        OnAvatarUpdated("GitLab", username);
                    }
                    else
                    {
                        lock (_cacheLock)
                        {
                            _pendingRequests.Remove(cacheKey); // Quitar de pending aunque falle
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (_cacheLock)
                    {
                        _pendingRequests.Remove(cacheKey); // Quitar de pending en caso de error
                    }
                }
            });

            return tempUrl;
        }

        /// <summary>
        /// Limpia el caché (útil al cerrar sesión)
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _avatarCache.Clear();
            }
        }

        /// <summary>
        /// Limpia el caché de un usuario específico
        /// </summary>
        public void ClearUserCache(string provider, string username)
        {
            lock (_cacheLock)
            {
                var keysToRemove = new List<string>();
                foreach (var key in _avatarCache.Keys)
                {
                    if (key.StartsWith($"{provider.ToLower()}:{username}:"))
                    {
                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _avatarCache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Precarga avatares para los proveedores especificados
        /// </summary>
        public async Task PreloadAvatarsAsync(ICredentialStorageService storage)
        {
            var providers = new[] { "GitHub", "GitLab" };
            foreach (var provider in providers)
            {
                var cred = await storage.GetCredentialAsync(provider);
                if (cred.HasValue && !string.IsNullOrWhiteSpace(cred.Value.username))
                {
                    if (provider == "GitHub")
                    {
                        GetGitHubAvatarUrl(cred.Value.username);
                    }
                    else if (provider == "GitLab")
                    {
                        await GetGitLabAvatarUrlAsync(cred.Value.username);
                    }
                }
            }
        }

        private string GetDefaultAvatarUrl(int size)
        {
            return $"https://www.gravatar.com/avatar/00000000000000000000000000000000?d=mp&s={size}";
        }
    }

    /// <summary>
    /// Argumentos del evento AvatarUpdated
    /// </summary>
    public class AvatarUpdatedEventArgs : EventArgs
    {
        public string Provider { get; }
        public string Username { get; }

        public AvatarUpdatedEventArgs(string provider, string username)
        {
            Provider = provider;
            Username = username;
        }
    }
}
