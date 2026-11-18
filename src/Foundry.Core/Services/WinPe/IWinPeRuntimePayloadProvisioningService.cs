// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public interface IWinPeRuntimePayloadProvisioningService
{
    /// <summary>
    /// Prepares every enabled archive, verifies release digests from one release snapshot, and pins the exact bytes for later stages.
    /// </summary>
    /// <param name="options">The payload sources and target architecture. Destinations may be assigned later.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Options containing reusable archive paths and pinned SHA256 hashes, or a failure.</returns>
    Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares the enabled archives when needed, then copies them into the mounted boot image: Foundry.WinPE into
    /// <c>Foundry\WinPE</c>, the Foundry.PostInstall launch files into <c>Foundry\PostInstall</c> and the debug
    /// Foundry.AutopilotInteractive archive into <c>Foundry\AutopilotInteractive</c>. Payloads of earlier media layouts are removed.
    /// </summary>
    /// <param name="options">The payload sources and the mounted image; <see cref="WinPeRuntimePayloadProvisioningOptions.MountedImagePath"/> is required.</param>
    /// <param name="downloadProgress">Receives release archive download progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
