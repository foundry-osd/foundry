using Microsoft.Win32;

namespace Foundry.Core.Services.Adk;

public sealed class WindowsAdkInstallationProbe : IAdkInstallationProbe
{
    private const string InstalledRootsKeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";
    private const string KitsRootValueName = "KitsRoot10";
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public string? GetKitsRootPath()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(InstalledRootsKeyPath);
        return key?.GetValue(KitsRootValueName) as string;
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryContainsFile(string directoryPath, string fileName)
    {
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(directoryPath, fileName, SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<AdkInstalledProduct> GetInstalledProducts()
    {
        List<AdkInstalledProduct> products = [];

        foreach (string keyPath in UninstallKeyPaths)
        {
            using RegistryKey? uninstallKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
            {
                using RegistryKey? productKey = uninstallKey.OpenSubKey(subKeyName);
                string? displayName = productKey?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                products.Add(new(
                    displayName,
                    productKey?.GetValue("DisplayVersion") as string,
                    productKey?.GetValue("UninstallString") as string,
                    productKey?.GetValue("QuietUninstallString") as string));
            }
        }

        return products;
    }
}
