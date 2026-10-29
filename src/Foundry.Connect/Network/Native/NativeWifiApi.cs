// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Provides the native WLAN API calls used by Foundry.Connect to discover, inspect and drive Wi-Fi state.
/// </summary>
/// <remarks>
/// Interface identifiers must be declared as <see langword="in"/> <see cref="Guid"/> because wlanapi expects
/// <c>const GUID*</c>. A by-value <see cref="Guid"/> only works on x64, where the calling convention happens to
/// pass 16-byte structures by reference; ARM64 passes them in registers and the calls fail.
/// Every import resolves <c>wlanapi.dll</c> from System32 only. Struct layouts mirror the Windows SDK headers.
/// </remarks>
internal static class NativeWifiApi
{
    internal const uint ClientVersion = 2;
    internal const uint AvailableNetworkIncludeAllAdhocProfiles = 0x00000001;
    private const int WlanMaxPhyTypeNumber = 8;

    // Values from the Windows SDK 10.0.26100.0 headers (l2cmn.h, wlanapi.h, winerror.h).
    private const int WlanMaxNameLength = 256;
    internal const uint NotificationSourceAcm = 0x8;
    internal const uint AcmConnectionComplete = 10;
    internal const uint AcmConnectionAttemptFail = 11;
    internal const uint AcmDisconnected = 21;
    internal const uint ProfileGetPlaintextKey = 0x4;
    internal const uint ErrorNotFound = 1168;
    internal const uint ErrorBadProfile = 1206;

    internal static void FreeMemory(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            WlanFreeMemory(pointer);
        }
    }

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out WlanClientHandle clientHandle);

    // Stays on IntPtr: WlanClientHandle.ReleaseHandle passes its raw handle here.
    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanCloseHandle(
        IntPtr clientHandle,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanEnumInterfaces(
        WlanClientHandle clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanGetAvailableNetworkList(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        uint flags,
        IntPtr reserved,
        out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanScan(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        IntPtr dot11Ssid,
        IntPtr ieData,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanQueryInterface(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        WlanIntfOpcode opCode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out WlanOpcodeValueType wlanOpcodeValueType);

    /// <summary>
    /// Adds or replaces a profile on one interface. <paramref name="flags"/> 0 stores an all-user profile;
    /// <see cref="ErrorBadProfile"/> comes with the WLAN reason in <paramref name="reasonCode"/>.
    /// </summary>
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanSetProfile(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string profileXml,
        [MarshalAs(UnmanagedType.LPWStr)] string? allUserProfileSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool overwrite,
        IntPtr reserved,
        out uint reasonCode);

    /// <summary>
    /// Reads a profile as XML allocated by wlanapi; free it with <see cref="WlanFreeMemory"/>.
    /// <see cref="ProfileGetPlaintextKey"/> in <paramref name="flags"/> asks for the key in plain text, which only an
    /// administrator (SYSTEM in WinPE) receives; otherwise the key stays encrypted and no error is returned.
    /// </summary>
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanGetProfile(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        [MarshalAs(UnmanagedType.LPWStr)] string profileName,
        IntPtr reserved,
        out IntPtr profileXml,
        ref uint flags,
        out uint grantedAccess);

    /// <summary>
    /// Starts a connection and returns at once; the outcome arrives later as an ACM notification.
    /// </summary>
    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanConnect(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        in WlanConnectionParameters connectionParameters,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanDisconnect(
        WlanClientHandle clientHandle,
        in Guid interfaceGuid,
        IntPtr reserved);

    /// <summary>
    /// Registers <paramref name="callback"/> for the given sources until the client handle is closed. The delegate
    /// must stay reachable for that whole time, and the callback must not call this function.
    /// </summary>
    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanRegisterNotification(
        WlanClientHandle clientHandle,
        uint notificationSource,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate,
        WlanNotificationCallback? callback,
        IntPtr callbackContext,
        IntPtr reserved,
        IntPtr previousNotificationSource);

    /// <summary>
    /// Writes the text of a WLAN reason code into <paramref name="buffer"/>; <paramref name="bufferSize"/> counts WCHARs.
    /// </summary>
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WlanReasonCodeToString(
        uint reasonCode,
        uint bufferSize,
        [Out] char[] buffer,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void WlanFreeMemory(IntPtr memory);

    /// <summary>
    /// <c>WLAN_NOTIFICATION_CALLBACK</c>: runs on a wlanapi thread; <paramref name="data"/> points to a
    /// <see cref="WlanNotificationData"/> that is valid only during the call.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WlanNotificationCallback(IntPtr data, IntPtr context);

    /// <summary>
    /// <c>WLAN_NOTIFICATION_DATA</c> (<c>L2_NOTIFICATION_DATA</c> in <c>l2cmn.h</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanNotificationData
    {
        public uint NotificationSource;
        public uint NotificationCode;
        public Guid InterfaceGuid;
        public uint DataSize;
        public IntPtr Data;
    }

    /// <summary>
    /// <c>WLAN_CONNECTION_PARAMETERS</c> (<c>wlanapi.h</c>); <see cref="Profile"/> is a caller-owned UTF-16 string.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanConnectionParameters
    {
        public int ConnectionMode;
        public IntPtr Profile;
        public IntPtr Ssid;
        public IntPtr DesiredBssidList;
        public int BssType;
        public uint Flags;
    }

    /// <summary>
    /// Fixed part of <c>WLAN_CONNECTION_NOTIFICATION_DATA</c> (<c>wlanapi.h</c>); the variable
    /// <c>strProfileXml</c> tail is not marshaled.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanConnectionNotificationHeader
    {
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WlanMaxNameLength)]
        public string ProfileName;

        public Dot11Ssid Ssid;
        public int BssType;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        public uint ReasonCode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanInterfaceInfoListHeader
    {
        public int NumberOfItems;
        public int Index;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InterfaceDescription;

        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanAvailableNetworkListHeader
    {
        public int NumberOfItems;
        public int Index;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public Dot11Ssid Ssid;
        public Dot11BssType BssType;
        public uint NumberOfBssids;

        [MarshalAs(UnmanagedType.Bool)]
        public bool NetworkConnectable;

        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = WlanMaxPhyTypeNumber)]
        public Dot11PhyType[] PhyTypes;

        [MarshalAs(UnmanagedType.Bool)]
        public bool MorePhyTypes;

        public uint SignalQuality;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        public Dot11AuthAlgorithm DefaultAuthAlgorithm;
        public Dot11CipherAlgorithm DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanConnectionAttributes
    {
        public WlanInterfaceState State;
        public WlanConnectionMode ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public Dot11BssType BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public Dot11PhyType PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool OneXEnabled;

        public Dot11AuthAlgorithm AuthAlgorithm;
        public Dot11CipherAlgorithm CipherAlgorithm;
    }

    internal enum WlanInterfaceState
    {
        NotReady = 0,
        Connected = 1,
        AdHocNetworkFormed = 2,
        Disconnecting = 3,
        Disconnected = 4,
        Associating = 5,
        Discovering = 6,
        Authenticating = 7
    }

    internal enum WlanConnectionMode
    {
        Profile = 0,
        TemporaryProfile = 1,
        DiscoverySecure = 2,
        DiscoveryUnsecure = 3,
        Auto = 4,
        Invalid = 5
    }

    internal enum WlanIntfOpcode
    {
        AutoconfStart = 0,
        AutoconfEnabled = 1,
        BackgroundScanEnabled = 2,
        MediaStreamingMode = 3,
        RadioState = 4,
        BssType = 5,
        InterfaceState = 6,
        CurrentConnection = 7
    }

    internal enum WlanOpcodeValueType
    {
        QueryOnly = 0,
        SetByGroupPolicy = 1,
        SetByUser = 2,
        Invalid = 3
    }

    internal enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    internal enum Dot11PhyType
    {
        Unknown = 0,
        Any = 0,
        Fhss = 1,
        Dsss = 2,
        IrBaseband = 3,
        Ofdm = 4,
        Hrdsss = 5,
        Erp = 6,
        Ht = 7,
        Vht = 8,
        Dmg = 9,
        He = 10,
        Eht = 11,
        IhvStart = unchecked((int)0x80000000),
        IhvEnd = unchecked((int)0xffffffff)
    }

    internal enum Dot11AuthAlgorithm : uint
    {
        Open80211 = 1,
        SharedKey = 2,
        Wpa = 3,
        WpaPsk = 4,
        WpaNone = 5,
        Rsna = 6,
        RsnaPsk = 7,
        Wpa3 = 8,
        Wpa3Sae = 9,
        Owe = 10,
        IhvStart = 0x80000000,
        IhvEnd = 0xffffffff
    }

    internal enum Dot11CipherAlgorithm : uint
    {
        None = 0x00,
        Wep40 = 0x01,
        Tkip = 0x02,
        Ccmp = 0x04,
        Wep104 = 0x05,
        Bip = 0x06,
        Gcmp = 0x08,
        Gcmp256 = 0x09,
        Ccmp256 = 0x0A,
        BipGmac128 = 0x0B,
        BipGmac256 = 0x0C,
        BipCmac256 = 0x0D,
        Wep = 0x101,
        IhvStart = 0x80000000,
        IhvEnd = 0xffffffff
    }
}
