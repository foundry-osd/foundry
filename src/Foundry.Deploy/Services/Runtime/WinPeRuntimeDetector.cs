using Microsoft.Win32;

namespace Foundry.Deploy.Services.Runtime;

internal static class WinPeRuntimeDetector
{
    private const string WinPeVersionRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinPE";
    private const string MiniNtRegistryKey = @"SYSTEM\CurrentControlSet\Control\MiniNT";

    public static bool IsWinPeRuntime()
    {
        return IsWinPeRuntime(
            Environment.GetEnvironmentVariable("SystemDrive"),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            RegistryKeyExists);
    }

    internal static bool IsWinPeRuntime(
        string? systemDrive,
        string? windowsDirectory,
        Func<string, bool> registryKeyExists)
    {
        ArgumentNullException.ThrowIfNull(registryKeyExists);

        if (registryKeyExists(WinPeVersionRegistryKey) || registryKeyExists(MiniNtRegistryKey))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(systemDrive) &&
            systemDrive.Equals("X:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(windowsDirectory) &&
               windowsDirectory.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegistryKeyExists(string subKeyPath)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKeyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
