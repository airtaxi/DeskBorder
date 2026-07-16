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
        var enqueueSuccess = dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                taskCompletionSource.SetResult();
            }
            catch (Exception exception) { taskCompletionSource.SetException(exception); }
        });
        if (!enqueueSuccess) taskCompletionSource.SetException(new InvalidOperationException("Failed to enqueue the UI operation."));

        return taskCompletionSource.Task;
    }

    public static Task ExecuteAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteAsync(async () =>
        {
            await action();
            return true;
        });
    }

    public static Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcherQueue = App.GetRequiredService<ManageWindow>().DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess) return action();

        var taskCompletionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueueSuccess = dispatcherQueue.TryEnqueue(async () =>
        {
            try { taskCompletionSource.SetResult(await action()); }
            catch (Exception exception) { taskCompletionSource.SetException(exception); }
        });
        if (!enqueueSuccess) taskCompletionSource.SetException(new InvalidOperationException("Failed to enqueue the UI operation."));

        return taskCompletionSource.Task;
    }
}
