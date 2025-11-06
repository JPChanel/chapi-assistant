using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Input;

namespace Chapi.Views.Dialogs
{
    public partial class InputDialog : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(InputDialog), new PropertyMetadata("Entrada"));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register("Message", typeof(string), typeof(InputDialog), new PropertyMetadata(string.Empty));

        public string Message
        {
            get { return (string)GetValue(MessageProperty); }
            set { SetValue(MessageProperty, value); }
        }

        public string ResponseText
        {
            get { return ResponseTextBox.Text; }
        }

        public InputDialog()
        {
            InitializeComponent();
            Loaded += (sender, e) => ResponseTextBox.Focus();
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ResponseTextBox.Text))
            {
                DialogHost.CloseDialogCommand.Execute(true, this);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogHost.CloseDialogCommand.Execute(false, this);
        }

        private void ResponseTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Pass null for the RoutedEventArgs as it is not used in the click handler.
                AcceptButton_Click(sender, null);
            }
        }
    }
}