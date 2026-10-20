// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Foundry.Deploy.Services.Hardware;

/// <summary>
/// Reads the firmware boot mode from the WinPE <c>PEFirmwareType</c> registry value, with the kernel firmware type as fallback.
/// </summary>
/// <remarks>
/// WinPE documents <c>HKLM\SYSTEM\CurrentControlSet\Control\PEFirmwareType</c> as 1 for BIOS and 2 for UEFI, but the value
/// is populated by <c>wpeutil UpdateBootInfo</c> and may be absent. <c>GetFirmwareType</c> reports the same boot mode from
/// the kernel and is used only when the registry value is missing or unrecognized.
/// </remarks>
public sealed class WinPeFirmwareBootModeReader : IFirmwareBootModeReader
{
    private const string ControlKeyPath = @"SYSTEM\CurrentControlSet\Control";
    private const string PeFirmwareTypeValueName = "PEFirmwareType";

    /// <inheritdoc />
    public FirmwareBootMode GetBootMode()
    {
        FirmwareBootMode registryMode = FromFirmwareType(ReadPeFirmwareType());
        return registryMode != FirmwareBootMode.Unknown ? registryMode : ReadKernelFirmwareType();
    }

    /// <summary>
    /// Maps a WinPE <c>PEFirmwareType</c> or kernel <c>FIRMWARE_TYPE</c> value, which share the same 1 and 2 encodings.
    /// </summary>
    /// <param name="value">The raw firmware type value.</param>
    /// <returns>The mapped boot mode, or <see cref="FirmwareBootMode.Unknown"/> for any other value.</returns>
    internal static FirmwareBootMode FromFirmwareType(object? value) => value switch
    {
        1 => FirmwareBootMode.Bios,
        2 => FirmwareBootMode.Uefi,
        _ => FirmwareBootMode.Unknown
    };

    private static object? ReadPeFirmwareType()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ControlKeyPath);
            return key?.GetValue(PeFirmwareTypeValueName);
        }
        catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FirmwareBootMode ReadKernelFirmwareType()
    {
        try
        {
            return GetFirmwareType(out int firmwareType) ? FromFirmwareType(firmwareType) : FirmwareBootMode.Unknown;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return FirmwareBootMode.Unknown;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out int firmwareType);
}
