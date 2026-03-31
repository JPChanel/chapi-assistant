using Chapi.Presentation.Alerts.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace Chapi.Presentation.Alerts
{
    public partial class NotificationHost : UserControl
    {
        public NotificationHost()
        {
            InitializeComponent();

            if (App.ServiceProvider != null)
            {
                DataContext = App.ServiceProvider.GetRequiredService<NotificationHostViewModel>();
            }
        }
    }
}
