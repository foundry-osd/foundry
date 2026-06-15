using System.Runtime.InteropServices;
using System.Text;
using Foundry.Deploy.Models;
using Microsoft.Win32;

namespace Foundry.Deploy.Services.Hardware;

internal sealed class WindowsHardwareProfileSource : IHardwareProfileSource
{
    private const string BiosRegistryPath = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpDevicedesc = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpMfg = 0x0000000B;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpClassGuid = 0x00000008;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public HardwareProfileSnapshot Capture()
    {
        IReadOnlyList<PnpDeviceInfo> devices = EnumeratePnpDevices();

        return new HardwareProfileSnapshot(
            Manufacturer: ReadBiosValue("SystemManufacturer"),
            Model: ReadBiosValue("SystemProductName"),
            Product: ReadBiosValue("SystemVersion"),
            SerialNumber: ReadBiosValue("SystemSerialNumber"),
            IsOnBattery: IsSystemOnBattery(),
            Devices: devices);
    }

    private static string ReadBiosValue(string name)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(BiosRegistryPath);
            return key?.GetValue(name)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSystemOnBattery()
    {
        return GetSystemPowerStatus(out SystemPowerStatus status) && status.ACLineStatus == 0;
    }

    private static IReadOnlyList<PnpDeviceInfo> EnumeratePnpDevices()
    {
        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidHandleValue)
        {
            return [];
        }

        try
        {
            var devices = new List<PnpDeviceInfo>();
            for (uint index = 0; ; index++)
            {
                SpDevinfoData deviceInfoData = new()
                {
                    CbSize = (uint)Marshal.SizeOf<SpDevinfoData>()
                };

                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData))
                {
                    break;
                }

                string friendlyName = ReadDeviceProperty(deviceInfoSet, ref deviceInfoData, SpdrpFriendlyName);
                string description = ReadDeviceProperty(deviceInfoSet, ref deviceInfoData, SpdrpDevicedesc);
                devices.Add(new PnpDeviceInfo
                {
                    Name = FirstNonEmpty(friendlyName, description),
                    DeviceId = ReadDeviceInstanceId(deviceInfoSet, ref deviceInfoData),
                    HardwareIds = ReadDeviceMultiStringProperty(deviceInfoSet, ref deviceInfoData, SpdrpHardwareId),
                    ClassGuid = ReadDeviceProperty(deviceInfoSet, ref deviceInfoData, SpdrpClassGuid),
                    Manufacturer = ReadDeviceProperty(deviceInfoSet, ref deviceInfoData, SpdrpMfg),
                    PnpClass = ReadDeviceProperty(deviceInfoSet, ref deviceInfoData, SpdrpClass)
                });
            }

            return devices;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string ReadDeviceProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property)
    {
        IReadOnlyList<string> values = ReadDeviceMultiStringProperty(deviceInfoSet, ref deviceInfoData, property);
        return values.Count > 0 ? values[0] : string.Empty;
    }

    private static IReadOnlyList<string> ReadDeviceMultiStringProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property)
    {
        byte[] buffer = new byte[8192];
        return SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref deviceInfoData,
            property,
            out _,
            buffer,
            (uint)buffer.Length,
            out uint requiredSize)
            ? DecodeRegistryString(buffer, requiredSize)
            : [];
    }

    private static string ReadDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData)
    {
        var buffer = new char[1024];
        return SetupDiGetDeviceInstanceId(
            deviceInfoSet,
            ref deviceInfoData,
            buffer,
            (uint)buffer.Length,
            out _)
            ? new string(buffer).TrimEnd('\0')
            : string.Empty;
    }

    private static IReadOnlyList<string> DecodeRegistryString(byte[] buffer, uint requiredSize)
    {
        if (requiredSize == 0)
        {
            return [];
        }

        int byteCount = Math.Min((int)requiredSize, buffer.Length);
        string raw = Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        char[] deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
}
