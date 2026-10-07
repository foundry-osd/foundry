// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Servicing;

/// <summary>Reports DISM progress without allowing managed exceptions across the native callback.</summary>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void DismProgressCallback(uint current, uint total, IntPtr userData);

/// <summary>Separates native allocations and synchronous DISM calls from worker ownership and policy.</summary>
internal interface IDismNativeApi
{
    /// <summary>Probes the supported exports without initializing or servicing.</summary>
    void EnsureAvailable();
    /// <summary>Initializes this worker once with its existing scratch and durable log paths.</summary>
    int Initialize(uint logLevel, string? logFilePath, string scratchDirectory);
    /// <summary>Ends initialization after the operation has released sessions and allocations.</summary>
    int Shutdown();
    /// <summary>Opens an offline target without mounting an already-applied Windows image.</summary>
    int OpenSession(string windowsRoot, out uint session);
    /// <summary>Releases the session before unmounting or shutting down.</summary>
    int CloseSession(uint session);
    /// <summary>Returns a packed SDK feature array owned by DISM until Delete is called.</summary>
    int GetFeatures(uint session, out IntPtr features, out uint count);
    /// <summary>Disables the feature and reports progress; the caller keeps payload removal disabled.</summary>
    int DisableFeature(uint session, string featureName, bool removePayload, DismProgressCallback progress);
    /// <summary>Adds one signed INF synchronously; this API has no callback or cancellation event.</summary>
    int AddDriver(uint session, string driverPath, bool forceUnsigned);
    /// <summary>Mounts the selected local WIM index using explicit SDK flags.</summary>
    int MountImage(string imagePath, uint imageIndex, string mountPath, uint flags, DismProgressCallback progress);
    /// <summary>Commits or discards an owned mount after all servicing sessions have closed.</summary>
    int UnmountImage(string mountPath, uint flags, DismProgressCallback progress);
    /// <summary>Returns registrations including invalid or remount-needed images in an owned native array.</summary>
    int GetMountedImages(out IntPtr images, out uint count);
    /// <summary>Captures the current thread's native diagnostic before another DISM call can replace it.</summary>
    int GetLastErrorMessage(out IntPtr message);
    /// <summary>Releases a DISM allocation; failure must not prevent other resource cleanup.</summary>
    int Delete(IntPtr allocation);
}
