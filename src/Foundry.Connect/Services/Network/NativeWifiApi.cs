using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Services.Network;

internal static class NativeWifiApi
{
    private const uint ClientVersion = 2;
    private const uint AvailableNetworkIncludeAllAdhocProfiles = 0x00000001;
    private const int WlanMaxPhyTypeNumber = 8;

    internal sealed record WifiInterfaceConnectionInfo(
        Guid InterfaceId,
        string InterfaceDescription,
        WlanInterfaceState State,
        string? CurrentSsid);

    public static bool IsRuntimeAvailable()
    {
        IntPtr clientHandle = IntPtr.Zero;

        try
        {
            clientHandle = OpenClientHandle();
            return true;
        }
        finally
        {
            CloseClientHandle(clientHandle);
        }
    }

    public static IReadOnlyList<WifiNetworkSummary> GetAvailableNetworks()
    {
        IntPtr clientHandle = IntPtr.Zero;
        IntPtr interfaceListPointer = IntPtr.Zero;

        try
        {
            clientHandle = OpenClientHandle();
            ThrowIfError(
                WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceListPointer),
                nameof(WlanEnumInterfaces));

            List<Guid> interfaceIds = ReadInterfaceIds(interfaceListPointer);
            Dictionary<string, WifiNetworkSummary> networks = new(StringComparer.OrdinalIgnoreCase);

        foreach (Guid interfaceId in interfaceIds)
        {
            IReadOnlyList<WifiNetworkSummary> interfaceNetworks = ReadAvailableNetworks(clientHandle, interfaceId);
            TryStartScan(clientHandle, interfaceId);

            foreach (WifiNetworkSummary network in interfaceNetworks)
            {
                if (networks.TryGetValue(network.Ssid, out WifiNetworkSummary? existing) &&
                    existing.SignalStrengthPercent >= network.SignalStrengthPercent)
                    {
                        continue;
                    }

                    networks[network.Ssid] = network;
                }
            }

            return networks.Values
                .OrderByDescending(static network => network.SignalStrengthPercent)
                .ThenBy(static network => network.Ssid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            FreeMemory(interfaceListPointer);
            CloseClientHandle(clientHandle);
        }
    }

    public static WifiInterfaceConnectionInfo? GetInterfaceConnectionInfo(Guid interfaceId)
    {
        IntPtr clientHandle = IntPtr.Zero;
        IntPtr interfaceListPointer = IntPtr.Zero;

        try
        {
            clientHandle = OpenClientHandle();
            ThrowIfError(
                WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceListPointer),
                nameof(WlanEnumInterfaces));

            if (interfaceListPointer == IntPtr.Zero)
            {
                return null;
            }

            WlanInterfaceInfoListHeader header = Marshal.PtrToStructure<WlanInterfaceInfoListHeader>(interfaceListPointer);
            int itemOffset = Marshal.SizeOf<WlanInterfaceInfoListHeader>();
            int itemSize = Marshal.SizeOf<WlanInterfaceInfo>();

            for (int index = 0; index < header.NumberOfItems; index++)
            {
                IntPtr itemPointer = IntPtr.Add(interfaceListPointer, itemOffset + (index * itemSize));
                WlanInterfaceInfo item = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
                if (item.InterfaceGuid != interfaceId)
                {
                    continue;
                }

                return new WifiInterfaceConnectionInfo(
                    item.InterfaceGuid,
                    item.InterfaceDescription,
                    item.State,
                    TryReadCurrentConnectionSsid(clientHandle, item.InterfaceGuid));
            }

            return null;
        }
        finally
        {
            FreeMemory(interfaceListPointer);
            CloseClientHandle(clientHandle);
        }
    }

    public static IReadOnlyList<Guid> GetInterfaceIds()
    {
        IntPtr clientHandle = IntPtr.Zero;
        IntPtr interfaceListPointer = IntPtr.Zero;

        try
        {
            clientHandle = OpenClientHandle();
            ThrowIfError(
                WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceListPointer),
                nameof(WlanEnumInterfaces));

            if (interfaceListPointer == IntPtr.Zero)
            {
                return [];
            }

            return ReadInterfaceIds(interfaceListPointer);
        }
        finally
        {
            FreeMemory(interfaceListPointer);
            CloseClientHandle(clientHandle);
        }
    }

    private static List<Guid> ReadInterfaceIds(IntPtr interfaceListPointer)
    {
        if (interfaceListPointer == IntPtr.Zero)
        {
            return [];
        }

        WlanInterfaceInfoListHeader header = Marshal.PtrToStructure<WlanInterfaceInfoListHeader>(interfaceListPointer);
        List<Guid> interfaceIds = new(header.NumberOfItems);
        int itemOffset = Marshal.SizeOf<WlanInterfaceInfoListHeader>();
        int itemSize = Marshal.SizeOf<WlanInterfaceInfo>();

        for (int index = 0; index < header.NumberOfItems; index++)
        {
            IntPtr itemPointer = IntPtr.Add(interfaceListPointer, itemOffset + (index * itemSize));
            WlanInterfaceInfo item = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
            interfaceIds.Add(item.InterfaceGuid);
        }

        return interfaceIds;
    }

    private static IReadOnlyList<WifiNetworkSummary> ReadAvailableNetworks(IntPtr clientHandle, Guid interfaceId)
    {
        IntPtr availableNetworkListPointer = IntPtr.Zero;

        try
        {
            ThrowIfError(
                WlanGetAvailableNetworkList(
                    clientHandle,
                    interfaceId,
                    AvailableNetworkIncludeAllAdhocProfiles,
                    IntPtr.Zero,
                    out availableNetworkListPointer),
                nameof(WlanGetAvailableNetworkList));

            if (availableNetworkListPointer == IntPtr.Zero)
            {
                return [];
            }

            WlanAvailableNetworkListHeader header = Marshal.PtrToStructure<WlanAvailableNetworkListHeader>(availableNetworkListPointer);
            List<WifiNetworkSummary> networks = new(header.NumberOfItems);
            int itemOffset = Marshal.SizeOf<WlanAvailableNetworkListHeader>();
            int itemSize = Marshal.SizeOf<WlanAvailableNetwork>();

            for (int index = 0; index < header.NumberOfItems; index++)
            {
                IntPtr itemPointer = IntPtr.Add(availableNetworkListPointer, itemOffset + (index * itemSize));
                WlanAvailableNetwork item = Marshal.PtrToStructure<WlanAvailableNetwork>(itemPointer);
                networks.Add(new WifiNetworkSummary
                {
                    Ssid = ReadSsid(item.Ssid),
                    SignalStrengthPercent = (int)Math.Clamp(item.SignalQuality, 0u, 100u),
                    Authentication = FormatAuthentication(item.DefaultAuthAlgorithm, item.SecurityEnabled),
                    Encryption = FormatEncryption(item.DefaultCipherAlgorithm, item.SecurityEnabled)
                });
            }

            return networks;
        }
        finally
        {
            FreeMemory(availableNetworkListPointer);
        }
    }

    private static string ReadSsid(Dot11Ssid ssid)
    {
        if (ssid.Length == 0 || ssid.Value.Length == 0)
        {
            return "Hidden network";
        }

        int length = (int)Math.Min(ssid.Length, (uint)ssid.Value.Length);
        string decoded = Encoding.UTF8.GetString(ssid.Value, 0, length).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? "Hidden network" : decoded;
    }

    private static string FormatAuthentication(Dot11AuthAlgorithm algorithm, bool securityEnabled)
    {
        if (!securityEnabled)
        {
            return "Open";
        }

        return algorithm switch
        {
            Dot11AuthAlgorithm.IhvStart or Dot11AuthAlgorithm.IhvEnd => "Vendor-specific",
            Dot11AuthAlgorithm.Open80211 => "Open",
            Dot11AuthAlgorithm.SharedKey => "WEP",
            Dot11AuthAlgorithm.Wpa => "WPA-Enterprise",
            Dot11AuthAlgorithm.WpaPsk => "WPA-Personal",
            Dot11AuthAlgorithm.WpaNone => "WPA-None",
            Dot11AuthAlgorithm.Rsna => "WPA2-Enterprise",
            Dot11AuthAlgorithm.RsnaPsk => "WPA2-Personal",
            Dot11AuthAlgorithm.Wpa3 => "WPA3-Enterprise",
            Dot11AuthAlgorithm.Wpa3Sae => "WPA3-Personal",
            Dot11AuthAlgorithm.Owe => "OWE",
            _ => $"Unknown ({(uint)algorithm})"
        };
    }

    private static string FormatEncryption(Dot11CipherAlgorithm algorithm, bool securityEnabled)
    {
        if (!securityEnabled)
        {
            return "None";
        }

        return algorithm switch
        {
            Dot11CipherAlgorithm.None => "None",
            Dot11CipherAlgorithm.Wep40 => "WEP40",
            Dot11CipherAlgorithm.Tkip => "TKIP",
            Dot11CipherAlgorithm.Ccmp => "CCMP",
            Dot11CipherAlgorithm.Wep104 => "WEP104",
            Dot11CipherAlgorithm.Wep => "WEP",
            Dot11CipherAlgorithm.Gcmp => "GCMP",
            Dot11CipherAlgorithm.Gcmp256 => "GCMP-256",
            Dot11CipherAlgorithm.Ccmp256 => "CCMP-256",
            Dot11CipherAlgorithm.BipGmac128 => "BIP-GMAC-128",
            Dot11CipherAlgorithm.BipGmac256 => "BIP-GMAC-256",
            Dot11CipherAlgorithm.BipCmac256 => "BIP-CMAC-256",
            Dot11CipherAlgorithm.IhvStart or Dot11CipherAlgorithm.IhvEnd => "Vendor-specific",
            _ => $"Unknown ({(uint)algorithm})"
        };
    }

    private static void TryStartScan(IntPtr clientHandle, Guid interfaceId)
    {
        _ = WlanScan(clientHandle, interfaceId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private static IntPtr OpenClientHandle()
    {
        ThrowIfError(
            WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out IntPtr clientHandle),
            nameof(WlanOpenHandle));

        return clientHandle;
    }

    private static void CloseClientHandle(IntPtr clientHandle)
    {
        if (clientHandle == IntPtr.Zero)
        {
            return;
        }

        _ = WlanCloseHandle(clientHandle, IntPtr.Zero);
    }

    private static void FreeMemory(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            WlanFreeMemory(pointer);
        }
    }

    private static void ThrowIfError(uint errorCode, string operation)
    {
        if (errorCode != 0)
        {
            throw new Win32Exception((int)errorCode, $"{operation} failed with error code {errorCode}.");
        }
    }

    private static string? TryReadCurrentConnectionSsid(IntPtr clientHandle, Guid interfaceId)
    {
        IntPtr dataPointer = IntPtr.Zero;

        try
        {
            uint result = WlanQueryInterface(
                clientHandle,
                interfaceId,
                WlanIntfOpcode.CurrentConnection,
                IntPtr.Zero,
                out _,
                out dataPointer,
                out _);
            if (result != 0 || dataPointer == IntPtr.Zero)
            {
                return null;
            }

            WlanConnectionAttributes attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
            return ReadSsid(attributes.AssociationAttributes.Ssid);
        }
        finally
        {
            FreeMemory(dataPointer);
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(
        IntPtr clientHandle,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(
        IntPtr clientHandle,
        Guid interfaceGuid,
        uint flags,
        IntPtr reserved,
        out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(
        IntPtr clientHandle,
        Guid interfaceGuid,
        IntPtr dot11Ssid,
        IntPtr ieData,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        Guid interfaceGuid,
        WlanIntfOpcode opCode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out WlanOpcodeValueType wlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanInterfaceInfoListHeader
    {
        public int NumberOfItems;
        public int Index;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InterfaceDescription;

        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAvailableNetworkListHeader
    {
        public int NumberOfItems;
        public int Index;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
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
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState State;
        public WlanConnectionMode ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
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
    private struct WlanSecurityAttributes
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

    private enum WlanConnectionMode
    {
        Profile = 0,
        TemporaryProfile = 1,
        DiscoverySecure = 2,
        DiscoveryUnsecure = 3,
        Auto = 4,
        Invalid = 5
    }

    private enum WlanIntfOpcode
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

    private enum WlanOpcodeValueType
    {
        QueryOnly = 0,
        SetByGroupPolicy = 1,
        SetByUser = 2,
        Invalid = 3
    }

    private enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    private enum Dot11PhyType
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

    private enum Dot11AuthAlgorithm : uint
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

    private enum Dot11CipherAlgorithm : uint
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
