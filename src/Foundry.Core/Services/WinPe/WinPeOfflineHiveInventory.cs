// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Win32;

namespace Foundry.Core.Services.WinPe;

/// <summary>A boot image registry hive that Foundry loaded under <c>HKLM</c> and that is still loaded.</summary>
/// <param name="KeyName">Name of the <c>HKLM</c> subkey the hive is loaded under.</param>
/// <param name="FilePath">Backing hive file in the NT device form Windows reports, such as <c>\Device\HarddiskVolume3\...\SOFTWARE</c>.</param>
internal sealed record WinPeLoadedHive(string KeyName, string FilePath);

/// <summary>
/// Lists the offline SOFTWARE hives that <see cref="WinPeBootBackgroundProvisioner"/> loads, from the hive list that
/// Windows keeps for every loaded hive. The list also names each backing file, which lets cleanup recovery tie a hive
/// to the operation whose mounted image holds it.
/// </summary>
internal static class WinPeOfflineHiveInventory
{
    private const string HiveListKeyPath = @"SYSTEM\CurrentControlSet\Control\hivelist";
    private const string MachineKeyPrefix = @"\REGISTRY\MACHINE\";

    /// <summary>
    /// Returns the loaded hives whose <c>HKLM</c> key name is the Foundry prefix followed by a GUID, so every returned
    /// name is one that Foundry created and is safe to pass to reg.exe.
    /// </summary>
    internal static IReadOnlyList<WinPeLoadedHive> GetLoadedHives()
    {
        using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? hiveList = localMachine.OpenSubKey(HiveListKeyPath);
        if (hiveList is null) return [];

        var hives = new List<WinPeLoadedHive>();
        foreach (string valueName in hiveList.GetValueNames())
        {
            if (!valueName.StartsWith(MachineKeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string keyName = valueName[MachineKeyPrefix.Length..];
            if (!keyName.StartsWith(WinPeBootBackgroundProvisioner.OfflineSoftwareHiveKeyPrefix, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(keyName[WinPeBootBackgroundProvisioner.OfflineSoftwareHiveKeyPrefix.Length..], "N", out _))
                continue;

            if (hiveList.GetValue(valueName) is string filePath && !string.IsNullOrWhiteSpace(filePath))
                hives.Add(new WinPeLoadedHive(keyName, filePath));
        }

        return hives;
    }
}
