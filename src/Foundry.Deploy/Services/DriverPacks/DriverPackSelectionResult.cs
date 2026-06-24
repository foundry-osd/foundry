// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record DriverPackSelectionResult
{
    public DriverPackCatalogItem? DriverPack { get; init; }
    public required string SelectionReason { get; init; }
}
