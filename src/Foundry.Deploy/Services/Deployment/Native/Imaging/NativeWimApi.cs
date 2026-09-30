// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Imaging;

/// <summary>Uses the host's architecture-matching imaging API; no bundled or current-directory DLL search.</summary>
internal sealed class NativeWimApi : INativeWimApi
{
    private static readonly string[] RequiredExports =
    [
        "WIMCreateFile", "WIMSetTemporaryPath", "WIMLoadImage", "WIMApplyImage", "WIMCloseHandle",
        "WIMRegisterMessageCallback", "WIMUnregisterMessageCallback", "WIMRegisterLogFile", "WIMUnregisterLogFile"
    ];

    public void Probe()
    {
        nint library;
        try
        {
            library = NativeLibrary.Load("wimgapi.dll", typeof(NativeWimApi).Assembly, DllImportSearchPath.System32);
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            int error = exception is BadImageFormatException ? 193 : 126;
            throw new NativeOperationException("LoadLibraryW", error, "The operating-system WIMGAPI library could not be loaded.", exception);
        }
        try
        {
            foreach (string export in RequiredExports)
                if (!NativeLibrary.TryGetExport(library, export, out _))
                    throw new NativeOperationException("GetProcAddress", 127, $"The operating-system WIMGAPI library does not export {export}.");
        }
        finally { NativeLibrary.Free(library); }
    }

    public int GetLastError() => Marshal.GetLastPInvokeError();
    public nint CreateFile(string path, uint access, uint disposition, uint flags, uint compression)
        => WIMCreateFile(path, access, disposition, flags, compression, nint.Zero);
    public bool SetTemporaryPath(nint wim, string path) => WIMSetTemporaryPath(wim, path);
    public nint LoadImage(nint wim, uint index) => WIMLoadImage(wim, index);
    public bool ApplyImage(nint image, string target, uint flags) => WIMApplyImage(image, target, flags);
    public uint RegisterMessageCallback(nint wim, nint callback) => WIMRegisterMessageCallback(wim, callback, nint.Zero);
    public bool UnregisterMessageCallback(nint wim, nint callback) => WIMUnregisterMessageCallback(wim, callback);
    public bool CloseHandle(nint handle) => WIMCloseHandle(handle);
    public bool RegisterLogFile(string path, uint flags) => WIMRegisterLogFile(path, flags);
    public bool UnregisterLogFile(string path) => WIMUnregisterLogFile(path);

    // The Windows ADK SDK's WIMGAPI.H defines WINAPI, 32-bit DWORD/BOOL and pointer-width HANDLE/FARPROC.
    [DllImport("wimgapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint WIMCreateFile(string path, uint access, uint disposition, uint flags, uint compression, nint creationResult);

    [DllImport("wimgapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMSetTemporaryPath(nint wim, string path);

    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint WIMLoadImage(nint wim, uint index);

    [DllImport("wimgapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMApplyImage(nint image, string target, uint flags);

    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WIMRegisterMessageCallback(nint wim, nint callback, nint userData);

    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMUnregisterMessageCallback(nint wim, nint callback);

    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMCloseHandle(nint handle);

    [DllImport("wimgapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMRegisterLogFile(string path, uint flags);

    [DllImport("wimgapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WIMUnregisterLogFile(string path);
}
