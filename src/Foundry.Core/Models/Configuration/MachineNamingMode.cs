// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines how a deployment computer name is supplied.
/// </summary>
public enum MachineNamingMode
{
    /// <summary>
    /// The deployment technician enters the complete computer name.
    /// </summary>
    Manual,

    /// <summary>
    /// Foundry composes the computer name from configured components.
    /// </summary>
    Composed
}
