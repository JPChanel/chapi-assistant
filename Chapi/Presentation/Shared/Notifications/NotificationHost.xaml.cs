using Chapi.Presentation.Shared.Notifications.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace Chapi.Presentation.Shared.Notifications
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
