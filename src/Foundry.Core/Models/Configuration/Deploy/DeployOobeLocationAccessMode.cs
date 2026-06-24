// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Defines how Foundry.Deploy configures Windows location access before first sign-in.
/// </summary>
public enum DeployOobeLocationAccessMode
{
    /// <summary>
    /// Leaves location choices available to the user after setup.
    /// </summary>
    UserControlled,

    /// <summary>
    /// Turns location services off and prevents apps from using device location.
    /// </summary>
    ForceOff
}
