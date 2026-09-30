// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Imaging;

/// <summary>Matches the pointer-width WIMGAPI callback ABI; managed exceptions must remain inside the callback.</summary>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate uint WimMessageCallback(uint message, nuint wParam, nint lParam, nint userData);

/// <summary>Separates synchronous imaging calls and native handle ownership from deployment workflow policy.</summary>
internal interface INativeWimApi
{
    void Probe();
    /// <summary>Reads the captured Win32 error immediately after the failed native call on the calling thread.</summary>
    int GetLastError();
    nint CreateFile(string path, uint access, uint disposition, uint flags, uint compression);
    /// <summary>Sets operation-owned scratch before loading an image.</summary>
    bool SetTemporaryPath(nint wim, string path);
    nint LoadImage(nint wim, uint index);
    bool ApplyImage(nint image, string target, uint flags);
    uint RegisterMessageCallback(nint wim, nint callback);
    /// <summary>Unregisters by function pointer, not the index returned by registration.</summary>
    bool UnregisterMessageCallback(nint wim, nint callback);
    bool CloseHandle(nint handle);
    bool RegisterLogFile(string path, uint flags);
    bool UnregisterLogFile(string path);
}
