// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Win32.SafeHandles;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Owns a wlanapi client handle opened by <c>WlanOpenHandle</c>.
/// </summary>
/// <remarks>
/// Releasing the handle calls <c>WlanCloseHandle</c>, which also ends every notification registration made on it
/// and waits for running callbacks to return.
/// </remarks>
internal sealed class WlanClientHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes an empty handle; the interop marshaler fills it from <c>WlanOpenHandle</c>.
    /// </summary>
    public WlanClientHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        return NativeWifiApi.WlanCloseHandle(handle, IntPtr.Zero) == 0;
    }
}
