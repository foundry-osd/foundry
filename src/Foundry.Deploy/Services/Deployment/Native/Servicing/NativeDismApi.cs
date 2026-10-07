// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Servicing;

/// <summary>Calls the documented DISM SDK ABI, resolving the operating-system library from System32 only.</summary>
internal sealed class NativeDismApi : IDismNativeApi
{
    private static readonly string[] RequiredExports =
    [
        "DismInitialize", "DismShutdown", "DismOpenSession", "DismCloseSession", "DismGetFeatures",
        "DismDisableFeature", "DismAddDriver", "DismMountImage", "DismUnmountImage",
        "DismGetMountedImageInfo", "DismGetLastErrorMessage", "DismDelete"
    ];

    /// <summary>Checks exports without initializing DISM or touching an image.</summary>
    public void EnsureAvailable()
    {
        IntPtr library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "DismApi.dll"),
            typeof(NativeDismApi).Assembly, DllImportSearchPath.System32);
        try
        {
            foreach (string export in RequiredExports) _ = NativeLibrary.GetExport(library, export);
        }
        finally { NativeLibrary.Free(library); }
    }

    /// <inheritdoc />
    public int Initialize(uint logLevel, string? logFilePath, string scratchDirectory) => DismInitialize(logLevel, logFilePath, scratchDirectory);
    /// <inheritdoc />
    public int Shutdown() => DismShutdown();
    /// <inheritdoc />
    public int OpenSession(string windowsRoot, out uint session) => DismOpenSession(windowsRoot, null, null, out session);
    /// <inheritdoc />
    public int CloseSession(uint session) => DismCloseSession(session);
    /// <inheritdoc />
    public int GetFeatures(uint session, out IntPtr features, out uint count) => DismGetFeatures(session, null, 0, out features, out count);
    /// <inheritdoc />
    public int DisableFeature(uint session, string featureName, bool removePayload, DismProgressCallback progress) =>
        DismDisableFeature(session, featureName, null, removePayload, IntPtr.Zero, progress, IntPtr.Zero);
    /// <inheritdoc />
    public int AddDriver(uint session, string driverPath, bool forceUnsigned) => DismAddDriver(session, driverPath, forceUnsigned);
    /// <inheritdoc />
    public int MountImage(string imagePath, uint imageIndex, string mountPath, uint flags, IntPtr cancelEvent, DismProgressCallback progress) =>
        DismMountImage(imagePath, mountPath, imageIndex, null, 0, flags, cancelEvent, progress, IntPtr.Zero);
    /// <inheritdoc />
    public int UnmountImage(string mountPath, uint flags, DismProgressCallback progress) =>
        DismUnmountImage(mountPath, flags, IntPtr.Zero, progress, IntPtr.Zero);
    /// <inheritdoc />
    public int GetMountedImages(out IntPtr images, out uint count) => DismGetMountedImageInfo(out images, out count);
    /// <inheritdoc />
    public int GetLastErrorMessage(out IntPtr message) => DismGetLastErrorMessage(out message);
    /// <inheritdoc />
    public int Delete(IntPtr allocation) => DismDelete(allocation);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismInitialize(uint logLevel, string? logFilePath, string scratchDirectory);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismShutdown();

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismOpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out uint session);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismCloseSession(uint session);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetFeatures(uint session, string? identifier, int packageIdentifier, out IntPtr features, out uint count);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismDisableFeature(uint session, string featureName, string? packageName,
        [MarshalAs(UnmanagedType.Bool)] bool removePayload, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismAddDriver(uint session, string driverPath, [MarshalAs(UnmanagedType.Bool)] bool forceUnsigned);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismMountImage(string imageFilePath, string mountPath, uint imageIndex, string? imageName,
        int imageIdentifier, uint flags, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

    [DllImport("DismApi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismUnmountImage(string mountPath, uint flags, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetMountedImageInfo(out IntPtr images, out uint count);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismGetLastErrorMessage(out IntPtr message);

    [DllImport("DismApi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DismDelete(IntPtr allocation);
}
