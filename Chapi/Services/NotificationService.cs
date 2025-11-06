namespace Chapi.Services
{
    public static class NotificationService
    {
        public static event Action<string> OnProgressUpdate;

        public static void SendProgressUpdate(string message)
        {
            OnProgressUpdate?.Invoke(message);
        }
    }
}
