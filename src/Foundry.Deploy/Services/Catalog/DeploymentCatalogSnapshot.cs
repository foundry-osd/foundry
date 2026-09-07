// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public sealed record DeploymentCatalogSnapshot(
    IReadOnlyList<OperatingSystemCatalogItem> OperatingSystems,
    IReadOnlyList<DriverPackCatalogItem> DriverPacks)
{
    public CatalogSnapshot<OperatingSystemCatalogItem>? OperatingSystemSnapshot { get; init; }
    public CatalogSnapshot<DriverPackCatalogItem>? DriverPackSnapshot { get; init; }
    public string? OperatingSystemFailure { get; init; }
    public string? DriverPackFailure { get; init; }
    public bool CanContinue { get; init; }
}
