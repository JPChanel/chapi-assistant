
using Chapi;
using Chapi.Helper.UserSettings;
using Chapi.Services;
using Chapi.Views.Dialogs;
using Mscc.GenerativeAI;

namespace AI.Clients;

public class AIClient
{
    /// <summary>
    /// Encapsula la conexión con la API de Gemini 
    /// </summary>
    /// 
   public static string GeminiApi
    {
        get
        {
            var settings = UserSettingsService.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {

                throw new Exception("No se encontró la API key de Gemini. Por favor, configúrela en la ventana de Servicios y Administración.");
            }
            return settings.GeminiApiKey;
        }
    }
    public static async Task<string> SendPromptAsync(string prompt)
    {
        var modelos = new[]
        {
            Model.Gemini25Flash,
            Model.Gemini20Flash
        };
        var msg = string.Empty;
        foreach (var modelo in modelos)
        {
            try
            {
                var googleAI = new GoogleAI(apiKey: GeminiApi);
                var model = googleAI.GenerativeModel(model: modelo);
                var response = await model.GenerateContent(prompt);

                var text = response.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                text = text.Trim();

                // Eliminar backticks de código si vienen en la respuesta
                if (text.StartsWith("```"))
                {
                    int start = text.IndexOf("{");
                    int end = text.LastIndexOf("}");
                    if (start >= 0 && end > start)
                        text = text.Substring(start, end - start + 1);
                }

                return text;
            }
            catch (Exception ex)
            {
                // Si hay error con este modelo, pasa al siguiente
                msg = ex.Message;
            }
        }
        if (!string.IsNullOrEmpty(msg))
        {
            await DialogService.ShowConfirmDialog("Alerta", msg, DialogVariant.Warning, DialogType.Info);

            Msg.Assistant(msg);
        }
        return string.Empty;
    }
}
