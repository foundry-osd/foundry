// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Imaging;

namespace Foundry.Core.Services.WinPe;

/// <summary>Loads only the operating-system DISM API to enumerate its mounted-image registrations.</summary>
internal sealed class NativeWinPeMountedImageApi : IWinPeMountedImageApi
{
    private IDisposable? lifetimeLease;

    /// <inheritdoc />
    public int Initialize()
    {
        lifetimeLease ??= DismApiLifetime.Shared.Acquire();
        return 0;
    }

    /// <inheritdoc />
    public int GetMountedImageInfo(out IntPtr imageInfo, out uint count)
        => DismGetMountedImageInfo(out imageInfo, out count);

    /// <inheritdoc />
    public int Delete(IntPtr imageInfo) => DismDelete(imageInfo);

    /// <inheritdoc />
    public int Shutdown()
    {
        Interlocked.Exchange(ref lifetimeLease, null)?.Dispose();
        return 0;
    }

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetMountedImageInfo(out IntPtr imageInfo, out uint count);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismDelete(IntPtr imageInfo);
}
