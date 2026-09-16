// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Calls the operating system DISM API without searching application or working directories for its DLL.
/// </summary>
internal sealed class NativeDismImageInfoApi : IDismImageInfoApi
{
    public int Initialize() => DismInitialize(0, null, null);

    public int GetImageInfo(string imagePath, out IntPtr imageInfo, out uint count)
        => DismGetImageInfo(imagePath, out imageInfo, out count);

    public int Delete(IntPtr imageInfo) => DismDelete(imageInfo);

    public int Shutdown() => DismShutdown();

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismInitialize(uint logLevel, string? logFilePath, string? scratchDirectory);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetImageInfo(string imageFilePath, out IntPtr imageInfo, out uint count);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismDelete(IntPtr imageInfo);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismShutdown();
}
