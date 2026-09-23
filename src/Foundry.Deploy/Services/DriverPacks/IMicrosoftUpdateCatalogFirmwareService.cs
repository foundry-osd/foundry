// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogFirmwareService
{
    /// <summary>Resolves and acquires the selected firmware CAB without extracting it.</summary>
    Task<MicrosoftUpdateCatalogFirmwareResult> DownloadAsync(
        HardwareProfile hardwareProfile,
        string targetArchitecture,
        string rawDirectory,
        Func<long, string, string> resolveCacheDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    /// <summary>Extracts acquired firmware CABs and requires a usable INF payload, awaiting extraction before cancellation.</summary>
    Task<int> ExtractAsync(
        string rawDirectory,
        string extractedDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
