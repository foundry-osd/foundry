// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Applies Windows time-zone rules without depending on desktop command-line tools.</summary>
internal static class WindowsTimeZone
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;
    private const uint ErrorNoMoreItems = 259;

    /// <summary>Temporarily enables the existing time-zone privilege and refreshes the .NET local-time cache after success.</summary>
    internal static void Set(string timeZoneId, CancellationToken cancellationToken)
    {
        TimeZoneInformation information = Find(timeZoneId, cancellationToken);
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        using (token)
        {
            if (!LookupPrivilegeValue(null, "SeTimeZonePrivilege", out Luid privilege))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var requested = new TokenPrivileges { Count = 1, Luid = privilege, Attributes = PrivilegeEnabled };
            bool adjusted = AdjustTokenPrivileges(token, false, ref requested, Marshal.SizeOf<TokenPrivileges>(), out TokenPrivileges previous, out _);
            int error = Marshal.GetLastWin32Error();
            if (!adjusted || error != 0) throw new Win32Exception(error);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                information.DynamicDaylightTimeDisabled = false;
                if (!SetDynamicTimeZoneInformation(ref information))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                TimeZoneInfo.ClearCachedData();
            }
            finally
            {
                // Restore the previous state, including a privilege that was already enabled.
                if (!AdjustTokenPrivileges(token, false, ref previous, Marshal.SizeOf<TokenPrivileges>(), out _, out _) ||
                    Marshal.GetLastWin32Error() != 0)
                    System.Diagnostics.Debug.WriteLine("Could not restore the time-zone privilege state.");
            }
        }
    }

    /// <summary>Reads the OS-provided DST rules and key name; never changes the current time zone.</summary>
    internal static TimeZoneInformation Find(string timeZoneId, CancellationToken cancellationToken)
    {
        for (uint index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint error = EnumDynamicTimeZoneInformation(index, out TimeZoneInformation information);
            if (error == ErrorNoMoreItems) throw new TimeZoneNotFoundException("The requested Windows time zone is unavailable.");
            if (error != 0) throw new Win32Exception(checked((int)error));
            if (string.Equals(information.TimeZoneKeyName, timeZoneId, StringComparison.OrdinalIgnoreCase)) return information;
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint EnumDynamicTimeZoneInformation(uint index, out TimeZoneInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDynamicTimeZoneInformation(ref TimeZoneInformation information);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges requested, int bufferLength, out TokenPrivileges previous, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TimeZoneInformation
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName;
        public SystemTime StandardDate;
        public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName;
        public SystemTime DaylightDate;
        public int DaylightBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string TimeZoneKeyName;
        [MarshalAs(UnmanagedType.U1)] public bool DynamicDaylightTimeDisabled;
    }
}
