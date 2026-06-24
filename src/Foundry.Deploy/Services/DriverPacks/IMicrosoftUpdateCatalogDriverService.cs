// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogDriverService
{
    Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        string destinationDirectory,
        string cacheDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
