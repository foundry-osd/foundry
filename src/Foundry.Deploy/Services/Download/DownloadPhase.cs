// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

/// <summary>
/// Identifies the work measured by artifact progress.
/// </summary>
public enum DownloadPhase
{
    /// <summary>Transferring an artifact from its source.</summary>
    Downloading,

    /// <summary>Hashing an existing artifact before accepting it from cache.</summary>
    VerifyingCache
}
