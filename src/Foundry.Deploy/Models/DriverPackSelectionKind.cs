// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

/// <summary>Exclusive driver source chosen in the deployment wizard.</summary>
public enum DriverPackSelectionKind
{
    /// <summary>No driver is injected.</summary>
    None = 0,

    /// <summary>Drivers are resolved from the Microsoft Update Catalog for the detected hardware.</summary>
    MicrosoftUpdateCatalog = 1,

    /// <summary>An OEM driver pack is downloaded from the manufacturer catalog.</summary>
    OemCatalog = 2,

    /// <summary>A custom driver set published on the boot media is injected.</summary>
    CustomDriverSet = 3
}
