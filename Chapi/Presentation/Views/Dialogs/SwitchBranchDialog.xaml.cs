using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace Chapi.Presentation.Views.Dialogs
{
    public partial class SwitchBranchDialog : UserControl
    {
        public static readonly DependencyProperty TargetBranchProperty =
            DependencyProperty.Register(nameof(TargetBranch), typeof(string), typeof(SwitchBranchDialog), new PropertyMetadata(string.Empty));

        public string TargetBranch
        {
            get => (string)GetValue(TargetBranchProperty);
            set => SetValue(TargetBranchProperty, value);
        }

        public bool ShouldStash => StashOption.IsChecked == true;

        public SwitchBranchDialog()
        {
            InitializeComponent();
        }

        private void SwitchBranch_Click(object sender, RoutedEventArgs e)
        {
            DialogHost.CloseDialogCommand.Execute(ShouldStash ? "stash" : "bring", null);
        }
    }
}

