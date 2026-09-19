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
        Func<long, string> resolveCacheDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
