// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;
using Foundry.Avalonia.Services.Threading;

namespace Foundry.Connect.Platform;

internal sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = _dispatcher.BeginInvoke(action);
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcher.CheckAccess()
            ? RunImmediately(action)
            : _dispatcher.InvokeAsync(action).Task;
    }

    private static Task RunImmediately(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
