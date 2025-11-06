using Chapi.Helper.GitHelper;
using Chapi.Helper.UserSettings;
using System.Net.NetworkInformation;

namespace Chapi.Services;

public class NetworkWatcherService
{
    private bool _isApplying = false;
    public static event Action OnProxyConfigChanged;
    // El dominio de tu oficina
    private string _corporateDomain = "pjudicial.pj.gob.pe";

    public NetworkWatcherService()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Task.Run(CheckNetworkAndApplyProxy);
    }
    /// <summary>
    /// Este método se dispara AUTOMÁTICAMENTE cuando Windows
    /// detecta un cambio en la red.
    /// </summary>
    private async void OnNetworkAddressChanged(object sender, EventArgs e)
    {
        // ¡La red cambió! Re-comprobamos el proxy.
        await CheckNetworkAndApplyProxy();
    }
    /// <summary>
    /// Comprueba la red actual y aplica o quita el proxy de Git.
    /// </summary>
    public async Task CheckNetworkAndApplyProxy()
    {
        if (_isApplying) return;
        _isApplying = true;
        bool configChanged = false;

        try
        {
            // ==========================================================
            // LÓGICA CORREGIDA:
            // 1. Lee las PREFERENCIAS guardadas por el usuario.
            // ==========================================================
            var settings = UserSettingsService.LoadSettings();
            string currentGitProxy = await Git.EjecutarGit("config --global http.proxy", "");

            bool isWifiActive = false;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    ni.OperationalStatus == OperationalStatus.Up)
                {
                    isWifiActive = true;
                    break;
                }
            }

            var currentDomain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            bool isOfficeDomain = currentDomain.EndsWith(_corporateDomain, StringComparison.OrdinalIgnoreCase);

            // REGLA 1: Si estamos en Wi-Fi (o el usuario lo deshabilitó)
            if (isWifiActive || !settings.ProxyEnabled)
            {
                if (!string.IsNullOrEmpty(currentGitProxy))
                {
                    await Git.EjecutarGit("config --global --unset http.proxy", "");
                    await Git.EjecutarGit("config --global --unset https.proxy", "");
                    configChanged = true;
                }
            }
            // REGLA 2: Si estamos en Ethernet Y en la oficina Y el usuario lo habilitó
            else if (!isWifiActive && isOfficeDomain && settings.ProxyEnabled)
            {
                if (string.IsNullOrWhiteSpace(settings.ProxyUrl))
                {
                    _isApplying = false;
                    return; 
                }

                string proxyUrlToApply = BuildProxyUrl(settings);
                if (currentGitProxy != proxyUrlToApply)
                {
                    await Git.EjecutarGit($"config --global http.proxy {proxyUrlToApply}", "");
                    await Git.EjecutarGit($"config --global https.proxy {proxyUrlToApply}", "");
                    configChanged = true;
                }
            }
            // REGLA 3: Si estamos en Ethernet pero en casa
            else
            {
                if (!string.IsNullOrEmpty(currentGitProxy))
                {
                    await Git.EjecutarGit("config --global --unset http.proxy", "");
                    await Git.EjecutarGit("config --global --unset https.proxy", "");
                    configChanged = true;
                }
            }
        }
        catch (Exception) { /* Falla silenciosa */ }
        finally
        {
            _isApplying = false;
            if (configChanged)
            {
                // Avisa a la UI que el config de Git cambió
                OnProxyConfigChanged?.Invoke();
            }
        }
    }
    /// <summary>
    /// Construye la URL del proxy con autenticación.
    /// </summary>
    private string BuildProxyUrl(UserApiSettings settings)
    {
        var url = settings.ProxyUrl; // ej: "proxyacl.pj.gob.pe:3128"
        var user = settings.ProxyUser;
        var pass = settings.ProxyPass;

        if (string.IsNullOrWhiteSpace(url)) return null;

        string scheme = "http"; // Asumir http
        if (url.StartsWith("http://"))
            url = url.Substring(7);
        else if (url.StartsWith("https://"))
        {
            scheme = "https";
            url = url.Substring(8);
        }

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            return $"{scheme}://{user}:{pass}@{url}";
        }
        return $"{scheme}://{url}";
    }
}
