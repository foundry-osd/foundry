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

    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
