using System.Windows;


namespace Chapi.Views.Dialogs
{
    /// <summary>
    /// Lógica de interacción para InfoDialog.xaml
    /// </summary>
    public enum DialogVariant
    {
        Info,
        Success,
        Warning,
        Error,
        Default
    }
    public partial class InfoDialog : System.Windows.Controls.UserControl
    {
        public InfoDialog()
        {
            InitializeComponent();
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(InfoDialog), new PropertyMetadata("Información"));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register("Message", typeof(string), typeof(InfoDialog), new PropertyMetadata(string.Empty));

        public string Message
        {
            get { return (string)GetValue(MessageProperty); }
            set { SetValue(MessageProperty, value); }
        }

        public static readonly DependencyProperty VariantProperty =
            DependencyProperty.Register("Variant", typeof(DialogVariant), typeof(InfoDialog), new PropertyMetadata(DialogVariant.Default));

        public DialogVariant Variant
        {
            get { return (DialogVariant)GetValue(VariantProperty); }
            set { SetValue(VariantProperty, value); }
        }
    }
}
