// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>
/// Runs work on the UI thread: inline when already on it, queued otherwise. View models marshal every callback that can
/// arrive on another thread (sign-in and registration progress, timers) through it.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. It never waits for a queued action to finish, and queued actions run
    /// in the order they were queued.
    /// </summary>
    /// <param name="action">The work to run.</param>
    void Run(Action action);
}

/// <summary>
/// <see cref="IUiDispatcher"/> backed by the WPF dispatcher of the UI thread.
/// </summary>
/// <remarks>
/// It must be given the UI thread's dispatcher (<c>Application.Current.Dispatcher</c>): a dispatcher created for a worker
/// thread never runs, and every queued action would be lost.
/// </remarks>
/// <param name="dispatcher">The UI thread's dispatcher.</param>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = _dispatcher.BeginInvoke(action);
    }
}

/// <summary>
/// <see cref="IProgress{T}"/> that delivers each report on the UI thread, in report order.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> is not used because it depends on the synchronization context of the thread that creates it.
/// </remarks>
/// <typeparam name="T">Type of the reported value.</typeparam>
/// <param name="dispatcher">Marshals each report to the UI thread.</param>
/// <param name="handler">Receives each report on the UI thread.</param>
internal sealed class DispatcherProgress<T>(IUiDispatcher dispatcher, Action<T> handler) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => dispatcher.Run(() => handler(value));
}
