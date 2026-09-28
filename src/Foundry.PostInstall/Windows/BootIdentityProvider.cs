// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace Foundry.PostInstall.Windows;

internal static class BootIdentityProvider
{
    public static string Read()
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\cimv2"), new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15) });
        using ManagementObjectCollection records = searcher.Get();
        foreach (ManagementObject record in records)
        {
            using (record)
            {
                string timestamp = record["LastBootUpTime"] as string ?? throw new InvalidDataException("Boot identity is unavailable.");
                return ManagementDateTimeConverter.ToDateTime(timestamp).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
        }
        throw new InvalidDataException("Boot identity is unavailable.");
    }

    public static bool IsSetupContext()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true) return false;
        using RegistryKey? setup = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup");
        return setup?.GetValue("SystemSetupInProgress") is int value && value == 1;
    }
}
