// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Updates;

namespace Foundry.Services.Application;

/// <summary>
/// Bridges application lifetime requests to the WinUI application instance.
/// </summary>
public sealed class WinUiApplicationLifetimeService : IApplicationLifetimeService, IApplicationUpdateRestartService
{
    /// <inheritdoc />
    public void Shutdown()
    {
        _ = App.Current.RequestCloseAsync(restartForUpdate: false);
    }

    /// <inheritdoc />
    public Task<bool> ApplyUpdateAsync() => App.Current.RequestCloseAsync(restartForUpdate: true);
}
