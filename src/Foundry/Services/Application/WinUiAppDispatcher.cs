using Foundry.Core.Services.Application;
using Microsoft.UI.Dispatching;

namespace Foundry.Services.Application;

/// <summary>
/// Marshals work onto the WinUI dispatcher queue.
/// </summary>
public sealed class WinUiAppDispatcher : IAppDispatcher
{
    /// <inheritdoc />
    public bool HasThreadAccess => GetDispatcherQueue().HasThreadAccess;

    /// <inheritdoc />
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

    /// <inheritdoc />
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
