// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// What started a network operation; logged as the <c>Trigger</c> property.
/// </summary>
public enum NetworkOperationTrigger
{
    /// <summary>The technician asked for it.</summary>
    Manual,

    /// <summary>The connection policy started it.</summary>
    Automatic,

    /// <summary>Connect started it once at startup.</summary>
    Startup
}
