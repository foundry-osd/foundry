// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public interface IWinPeRuntimePayloadProvisioningService
{
    /// <summary>
    /// Prepares local archives and captures one release snapshot for the media build.
    /// </summary>
    /// <param name="options">The payload sources and target architecture. Destinations may be assigned later.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Options containing prepared local archives and validated release metadata, or a failure.</returns>
    Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        CancellationToken cancellationToken = default);

    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
