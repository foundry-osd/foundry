// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public enum DriverPackExtractionMethod
{
    None = 0,
    SevenZip = 1,
    DellSelfExtractor = 2,
    MicrosoftUpdateCatalogExpand = 3,

    /// <summary>A custom driver set is copied from the boot media with per-file verification; nothing is extracted.</summary>
    CustomDriverSetCopy = 4
}
