// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Applies a prepared update through the application's protected save and close flow.
/// </summary>
public interface IApplicationUpdateRestartService
{
    /// <summary>
    /// Requests a visible update and restart, retaining the running application when closing is canceled or handoff fails.
    /// </summary>
    /// <returns>Whether the prepared update was handed off and application closing was committed.</returns>
    Task<bool> ApplyUpdateAsync();
}
