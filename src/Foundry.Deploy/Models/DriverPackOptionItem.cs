// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models;

public sealed record DriverPackOptionItem
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required DriverPackSelectionKind Kind { get; init; }
    public DriverPackCatalogItem? DriverPack { get; init; }
}
