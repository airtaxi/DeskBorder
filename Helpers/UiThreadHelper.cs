using DeskBorder.Views;

namespace DeskBorder.Helpers;

public static class UiThreadHelper
{
    public static Task ExecuteAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcherQueue = App.GetRequiredService<ManageWindow>().DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                taskCompletionSource.SetResult();
            }
            catch (Exception exception) { taskCompletionSource.SetException(exception); }
        }))
        {
            taskCompletionSource.SetException(new InvalidOperationException("Failed to enqueue the UI operation."));
        }

        return taskCompletionSource.Task;
    }
}
