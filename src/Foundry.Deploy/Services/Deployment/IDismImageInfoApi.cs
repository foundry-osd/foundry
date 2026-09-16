// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Provides the native DISM lifetime and buffer ownership boundary for image metadata reads.
/// </summary>
internal interface IDismImageInfoApi
{
    /// <summary>
    /// Initializes DISM once for the owning process before any metadata reads.
    /// </summary>
    int Initialize();

    /// <summary>
    /// Returns a native array whose non-null pointer the caller must release, including on failure.
    /// </summary>
    int GetImageInfo(string imagePath, out IntPtr imageInfo, out uint count);

    /// <summary>
    /// Releases an array and its nested allocations through DISM's allocator.
    /// </summary>
    int Delete(IntPtr imageInfo);

    /// <summary>
    /// Ends a successfully initialized DISM lifetime after active calls and buffer release have finished.
    /// </summary>
    int Shutdown();
}
