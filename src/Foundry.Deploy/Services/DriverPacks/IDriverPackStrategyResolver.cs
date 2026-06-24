// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

/// <summary>
/// Resolves the install and extraction strategy for a selected driver package.
/// </summary>
public interface IDriverPackStrategyResolver
{
    /// <summary>
    /// Resolves the driver pack execution plan for the selected package.
    /// </summary>
    /// <param name="selectionKind">Selected driver pack source.</param>
    /// <param name="driverPack">Selected catalog driver pack, when required by the source.</param>
    /// <param name="downloadedPath">Downloaded package path.</param>
    /// <returns>The execution plan used by deployment steps.</returns>
    DriverPackExecutionPlan Resolve(
        DriverPackSelectionKind selectionKind,
        DriverPackCatalogItem? driverPack,
        string downloadedPath);
}
