// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Updates;

namespace Foundry.Services.Application;

/// <summary>
/// Routes update restart requests to the application's protected close flow.
/// </summary>
public sealed class WinUiApplicationLifetimeService : IApplicationUpdateRestartService
{
    /// <inheritdoc />
    public Task<bool> ApplyUpdateAsync() => App.Current.RequestCloseAsync(restartForUpdate: true);
}
