using Chapi.Presentation.Alerts.Models;
using Chapi.Presentation.Alerts.Service;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Chapi.Infrastructure.Services;

public class MessageHelper : INotifyPropertyChanged
{
    private static MessageHelper _instance;
    public static MessageHelper Instance => _instance ??= new MessageHelper();
    public event Action<ChatMessage> MessageAdded;

    private ObservableCollection<ChatMessage> _messages = new();
    public ObservableCollection<ChatMessage> Messages
    {
        get => _messages;
        set
        {
            _messages = value;
            OnPropertyChanged(nameof(Messages));
        }
    }

    public void AddUserMessage(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var msg = new ChatMessage
            {
                Author = "User",
                Text = text,
                Timestamp = DateTime.Now.ToString("HH:mm")
            };
            Messages.Add(msg);
            MessageAdded?.Invoke(msg);
            ScrollRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void AddAssistantMessage(string text, bool showAlert = true)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var msg = new ChatMessage
            {
                Author = "Assistant",
                Text = text,
                Timestamp = DateTime.Now.ToString("HH:mm")
            };
            Messages.Add(msg);
            MessageAdded?.Invoke(msg);
            ScrollRequested?.Invoke(this, EventArgs.Empty);
        });

        if (showAlert)
        {
            TryShowAlert(text);
        }
    }

    private static void TryShowAlert(string text)
    {
        if (!AppServices.IsConfigured || AppServices.AlertService is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var variant = InferVariant(text);
        var title = variant switch
        {
            AlertVariant.Success => "Correcto",
            AlertVariant.Warning => "Aviso",
            AlertVariant.Error => "Error",
            _ => "Información"
        };

        var duration = variant switch
        {
            AlertVariant.Error => TimeSpan.FromSeconds(6),
            AlertVariant.Warning => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(4)
        };

        AppServices.AlertService.Show(text, title, variant, duration: duration);
    }

    private static AlertVariant InferVariant(string text)
    {
        var normalized = text.Trim();

        if (normalized.Contains("❌", StringComparison.Ordinal) ||
            normalized.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("fall", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("no se pudo", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Error;
        }

        if (normalized.Contains("⚠", StringComparison.Ordinal) ||
            normalized.Contains("advert", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("aviso", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Warning;
        }

        if (normalized.Contains("✅", StringComparison.Ordinal) ||
            normalized.Contains("exito", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("correctamente", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("completado", StringComparison.OrdinalIgnoreCase))
        {
            return AlertVariant.Success;
        }

        return AlertVariant.Info;
    }

    public event EventHandler ScrollRequested;

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public class ChatMessage : INotifyPropertyChanged
    {
        private string _text;
        public string Author { get; set; }
        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(nameof(Text)); }
        }
        public string Timestamp { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class Msg
{
    public static void User(string msg) => MessageHelper.Instance.AddUserMessage(msg);
    public static void Assistant(string msg, bool showAlert = true) => MessageHelper.Instance.AddAssistantMessage(msg, showAlert);
}
