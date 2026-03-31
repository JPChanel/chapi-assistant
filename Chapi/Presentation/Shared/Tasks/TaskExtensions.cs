using Chapi.Infrastructure.Services;

namespace Chapi.Presentation.Shared.Tasks;

public static class TaskExtensions
{
    public static void Forget(this Task? task, string context)
    {
        if (task == null)
        {
            return;
        }

        _ = ObserveAsync(task, context);
    }

    private static async Task ObserveAsync(Task task, string context)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            var message = string.IsNullOrWhiteSpace(context)
                ? $"Error en segundo plano: {root.Message}"
                : $"Error en segundo plano ({context}): {root.Message}";

            Msg.Assistant(message);
        }
    }
}
