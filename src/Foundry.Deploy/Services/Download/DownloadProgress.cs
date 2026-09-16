// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

/// <summary>
/// Reports bytes processed within one artifact phase. Counts restart when the phase changes.
/// </summary>
/// <param name="BytesProcessed">Bytes hashed or downloaded in the current phase.</param>
/// <param name="TotalBytes">Total bytes in the current phase, when known.</param>
/// <param name="Phase">Distinguishes cache verification from a network download.</param>
public readonly record struct DownloadProgress(
    long BytesProcessed,
    long? TotalBytes,
    DownloadPhase Phase = DownloadPhase.Downloading);
