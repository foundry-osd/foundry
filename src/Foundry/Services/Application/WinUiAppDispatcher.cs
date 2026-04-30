using Foundry.Core.Services.Application;
using Microsoft.UI.Dispatching;

namespace Foundry.Services.Application;

public sealed class WinUiAppDispatcher : IAppDispatcher
{
    public bool HasThreadAccess => GetDispatcherQueue().HasThreadAccess;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        DispatcherQueue dispatcherQueue = GetDispatcherQueue();
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        return dispatcherQueue.TryEnqueue(() => action());
    }

    public Task EnqueueAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        DispatcherQueue dispatcherQueue = GetDispatcherQueue();
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue work on the WinUI dispatcher."));
        }

        return completion.Task;
    }

    private static DispatcherQueue GetDispatcherQueue()
    {
        return App.MainWindow.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
    }
}
