using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Chapi.Helper;

public class MessageHelper : INotifyPropertyChanged
{
    private static MessageHelper _instance;
    public static MessageHelper Instance => _instance ??= new MessageHelper();

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
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage
        {
            Author = "User",
            Text = text,
            Timestamp = DateTime.Now.ToString("HH:mm")
        });

        ScrollRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void AddAssistantMessage(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage
            {
                Author = "Assistant",
                Text = text,
                Timestamp = DateTime.Now.ToString("HH:mm")
            });
            ScrollRequested?.Invoke(this, EventArgs.Empty);
        });
       
    }

    // 🔄 Evento que MainWindow puede escuchar para hacer scroll automático
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
    public static void Assistant(string msg) => MessageHelper.Instance.AddAssistantMessage(msg);
}