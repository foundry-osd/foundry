// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

/// <summary>
/// Bridges application lifetime requests to the WinUI application instance.
/// </summary>
public sealed class WinUiApplicationLifetimeService : IApplicationLifetimeService
{
    /// <inheritdoc />
    public void Shutdown()
    {
        if (App.MainWindow is Foundry.Views.MainWindow mainWindow)
        {
            mainWindow.RequestSafeClose();
        }
        else if (App.MainWindow.DispatcherQueue.HasThreadAccess)
        {
            App.MainWindow.Close();
        }
        else
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() => App.MainWindow.Close());
        }
    }
}
