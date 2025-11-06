
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Views.Dialogs
{

    public enum DialogType
    {
        Confirm,
        Info
    }

    public partial class ConfirmationDialog : UserControl
    {
        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(ConfirmationDialog), new PropertyMetadata("Confirmación"));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register("Message", typeof(string), typeof(ConfirmationDialog), new PropertyMetadata(string.Empty));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public static readonly DependencyProperty VariantProperty =
            DependencyProperty.Register("Variant", typeof(DialogVariant), typeof(ConfirmationDialog), new PropertyMetadata(DialogVariant.Info));

        public DialogVariant Variant
        {
            get => (DialogVariant)GetValue(VariantProperty);
            set => SetValue(VariantProperty, value);
        }

        public static readonly DependencyProperty DialogTypeProperty =
            DependencyProperty.Register("DialogType", typeof(DialogType), typeof(ConfirmationDialog), new PropertyMetadata(DialogType.Confirm));

        public DialogType DialogType
        {
            get => (DialogType)GetValue(DialogTypeProperty);
            set => SetValue(DialogTypeProperty, value);
        }
    }
}
