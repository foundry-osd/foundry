// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Management;
using System.Runtime.InteropServices;
using Foundry.PostInstall.Actions;
using Microsoft.Win32;

namespace Foundry.PostInstall.Windows;

internal sealed class WmiLicensing : IWindowsLicensing
{
    public bool IsClient
    {
        get
        {
            using var searcher = Search("SELECT ProductType FROM Win32_OperatingSystem");
            using ManagementObjectCollection records = searcher.Get();
            foreach (ManagementObject record in records) { using (record) return Convert.ToInt32(record["ProductType"]) == 1; }
            return false;
        }
    }

    public string Edition
    {
        get
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("EditionID") as string ?? string.Empty;
        }
    }

    public FirmwareLicense Firmware
    {
        get
        {
            using ManagementObject service = GetService();
            return new(Text(service, "OA3xOriginalProductKey"), Text(service, "OA3xOriginalProductKeyDescription"),
                Text(service, "KeyManagementServiceMachine"), Convert.ToInt32(service["IsKeyManagementServiceMachine"] ?? 0) == 1);
        }
    }

    public IReadOnlyList<LicensingProduct> GetProducts()
    {
        using var searcher = Search("SELECT * FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
        using ManagementObjectCollection records = searcher.Get();
        List<LicensingProduct> products = [];
        foreach (ManagementObject record in records)
        {
            using (record)
            {
                if (record["LicenseIsAddon"] is not bool addon || addon) continue;
                products.Add(new(Text(record, "ID"), Text(record, "LicenseFamily"),
                    record["LicenseStatus"] is null ? null : Convert.ToInt32(record["LicenseStatus"]),
                    Text(record, "ProductKeyChannel"), Text(record, "PartialProductKey"), Text(record, "Description"), Text(record, "KeyManagementServiceMachine")));
            }
        }
        return products;
    }

    public void InstallKey(string key)
    {
        using ManagementObject service = GetService();
        using ManagementBaseObject arguments = service.GetMethodParameters("InstallProductKey");
        arguments["ProductKey"] = key;
        Invoke(service, "InstallProductKey", arguments);
    }

    public void Refresh() { using ManagementObject service = GetService(); Invoke(service, "RefreshLicenseStatus", null); }

    public void Activate(string id)
    {
        if (!Guid.TryParse(id, out Guid identifier)) throw new InvalidDataException("Invalid licensing identity.");
        using var product = new ManagementObject($"SoftwareLicensingProduct.ID='{identifier:D}'");
        Invoke(product, "Activate", null);
    }

    private static void Invoke(ManagementObject instance, string method, ManagementBaseObject? arguments)
    {
        using ManagementBaseObject result = instance.InvokeMethod(method, arguments, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(15) });
        uint code = result["ReturnValue"] is uint value ? value : throw new InvalidDataException("Licensing status is unavailable.");
        if (code != 0) throw new COMException("Licensing provider operation failed.", unchecked((int)code));
    }

    private static ManagementObject GetService()
    {
        using var searcher = Search("SELECT * FROM SoftwareLicensingService");
        using ManagementObjectCollection records = searcher.Get();
        foreach (ManagementObject record in records) return record;
        throw new InvalidDataException("Licensing service is unavailable.");
    }

    private static ManagementObjectSearcher Search(string query) => new(new ManagementScope("root\\cimv2"), new ObjectQuery(query),
        new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15) });
    private static string Text(ManagementBaseObject value, string property) => value[property]?.ToString() ?? string.Empty;
}
