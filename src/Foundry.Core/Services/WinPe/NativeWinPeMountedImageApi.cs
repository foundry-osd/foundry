// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Core.Services.WinPe;

/// <summary>Loads only the operating-system DISM API to enumerate its mounted-image registrations.</summary>
internal sealed class NativeWinPeMountedImageApi : IWinPeMountedImageApi
{
    /// <inheritdoc />
    public int Initialize() => DismInitialize(0, null, null);

    /// <inheritdoc />
    public int GetMountedImageInfo(out IntPtr imageInfo, out uint count)
        => DismGetMountedImageInfo(out imageInfo, out count);

    /// <inheritdoc />
    public int Delete(IntPtr imageInfo) => DismDelete(imageInfo);

    /// <inheritdoc />
    public int Shutdown() => DismShutdown();

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismInitialize(uint logLevel, string? logFilePath, string? scratchDirectory);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetMountedImageInfo(out IntPtr imageInfo, out uint count);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismDelete(IntPtr imageInfo);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismShutdown();
}
