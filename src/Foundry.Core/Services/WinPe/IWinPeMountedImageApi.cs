// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Isolates process-wide DISM ownership and its allocated mounted-image inventory for deterministic testing.
/// </summary>
internal interface IWinPeMountedImageApi
{
    /// <summary>Acquires process-wide DISM ownership before the first inventory read.</summary>
    int Initialize();

    /// <summary>Returns the DISM-allocated array, including registrations that need recovery.</summary>
    int GetMountedImageInfo(out IntPtr imageInfo, out uint count);

    /// <summary>Releases a returned native array after its paths have been copied.</summary>
    int Delete(IntPtr imageInfo);

    /// <summary>Releases a successful initialization after all native reads have completed.</summary>
    int Shutdown();
}
