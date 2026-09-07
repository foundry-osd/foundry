// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public interface IWinPeRuntimePayloadProvisioningService
{
    /// <summary>Acquires each selected runtime once, validates it, and owns its files until disposal.</summary>
    Task<WinPeResult<WinPePreparedRuntimePayloads>> PrepareAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Checks prepared bytes before a caller begins destructive media operations.</summary>
    Task<WinPeResult> ValidatePreparedAsync(
        WinPePreparedRuntimePayloads prepared,
        CancellationToken cancellationToken = default);

    /// <summary>Places the recorded local files without downloading or publishing runtime projects.</summary>
    Task<WinPeResult> ProvisionPreparedAsync(
        WinPePreparedRuntimePayloads prepared,
        WinPeRuntimePayloadProvisioningOptions destinations,
        CancellationToken cancellationToken = default);

    /// <summary>Prepares, places, and disposes payloads for callers that do not share a preparation.</summary>
    Task<WinPeResult> ProvisionAsync(
        WinPeRuntimePayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default);
}
