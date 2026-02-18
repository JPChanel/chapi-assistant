using System.Windows;
using System.Windows.Input;

namespace Chapi.Presentation.Views.Dialogs;

public partial class ExecutionLogDialog : Window
{
    public ExecutionLogDialog()
    {
        InitializeComponent();

        // Auto-Scroll: Mantener el foco en el último ítem
        ((System.Collections.Specialized.INotifyCollectionChanged)LogListBox.Items).CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
