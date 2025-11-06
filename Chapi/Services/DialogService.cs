using Chapi.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace Chapi.Services
{
    public static class DialogService
    {
        public static async Task<(bool, string)> ShowInputDialog(string title, string message, string? defaultText = null)
        {
            var dialog = new InputDialog
            {
                Message = message,
                Title = title,

            };
            if (!string.IsNullOrEmpty(defaultText))
            {
                dialog.ResponseTextBox.Text = defaultText;
            }

            var result = await DialogHost.Show(dialog, App.GlobalDialogIdentifier);
            var res = bool.TryParse(result?.ToString(), out var boolResult) && boolResult;

            return (res, dialog.ResponseText ?? string.Empty);

        }

        public static async Task<bool> ShowConfirmDialog(string title, string message, DialogVariant variant = DialogVariant.Info, DialogType type = DialogType.Confirm)
        {
            var dialog = new ConfirmationDialog
            {
                Title = title,
                Message = message,
                Variant = variant,
                DialogType = type
            };

            var result = await DialogHost.Show(dialog, App.GlobalDialogIdentifier);


            return bool.TryParse(result?.ToString(), out var boolResult) && boolResult;
        }
        public static void ShowTrayNotification(string title, string message)
        {
            App.TrayIconManager.ShowNotification(title, message);
        }
    }
}
