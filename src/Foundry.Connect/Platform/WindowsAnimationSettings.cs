// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Connect.Platform;

internal static class WindowsAnimationSettings
{
    private const uint GetClientAreaAnimation = 0x1042;

    public static bool IsEnabled()
    {
        bool isEnabled = true;
        return SystemParametersInfo(GetClientAreaAnimation, 0, ref isEnabled, 0) ? isEnabled : true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] ref bool value,
        uint updateFlags);
}
