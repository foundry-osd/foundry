// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// <see cref="IUiDispatcher"/> backed by the WPF dispatcher of the UI thread.
/// </summary>
/// <remarks>
/// The instance runs work on the dispatcher it is given, so it must be given the UI thread's dispatcher:
/// <c>Application.Current.Dispatcher</c>, or <see cref="Dispatcher.CurrentDispatcher"/> read on the UI thread.
/// <see cref="Dispatcher.CurrentDispatcher"/> read on a worker thread would create a dispatcher for that thread that
/// never runs, and every queued action would be lost. The DI registration passes <c>Application.Current.Dispatcher</c>.
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

        _dispatcher.BeginInvoke(action);
    }
}
