// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Defines image inspection, application and offline driver servicing.</summary>
public interface IWindowsImagingService : IWindowsImageInspectionService
{
    /// <summary>
    /// Resolves the WIM/ESD image index matching a requested edition.
    /// </summary>
    /// <param name="imagePath">Path to the WIM or ESD image.</param>
    /// <param name="requestedEdition">Windows edition name requested by the catalog.</param>
    /// <param name="workingDirectory">Directory used for temporary scripts and command output.</param>
    /// <param name="cancellationToken">Token that cancels image inspection.</param>
    /// <returns>The image index matching the requested edition.</returns>
    Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a Windows image to the target Windows partition.
    /// </summary>
    /// <param name="imagePath">Path to the WIM or ESD image.</param>
    /// <param name="imageIndex">Image index to apply.</param>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="scratchDirectory">DISM scratch directory used during image apply.</param>
    /// <param name="workingDirectory">Directory used for temporary scripts and command output.</param>
    /// <param name="cancellationToken">Token that cancels image application.</param>
    /// <param name="progress">Optional progress sink for DISM percentage updates.</param>
    /// <returns>A task that completes after the image is applied.</returns>
    Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    /// <summary>
    /// Reads the currently applied Windows edition from the offline image.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="workingDirectory">Directory used for temporary command output.</param>
    /// <param name="cancellationToken">Token that cancels edition inspection.</param>
    /// <returns>The applied Windows edition, or <see langword="null"/> when it cannot be resolved.</returns>
    Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects INF drivers into the offline Windows image.
    /// </summary>
    /// <param name="windowsPartitionRoot">Root path of the target Windows partition.</param>
    /// <param name="driverRoot">Root directory containing extracted INF drivers.</param>
    /// <param name="scratchDirectory">DISM scratch directory used during driver injection.</param>
    /// <param name="workingDirectory">Directory used for temporary scripts and command output.</param>
    /// <param name="cancellationToken">Token that cancels driver injection.</param>
    /// <param name="progress">Optional progress sink for DISM percentage updates.</param>
    /// <returns>A task that completes after offline drivers are applied.</returns>
    Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
