using System.IO;
using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Configuration;

internal static class ProvisionedWifiProfileResolver
{
    public static string? ResolveAssetPath(string? value, string? configurationPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        string? configurationDirectoryPath = null;
        if (!string.IsNullOrWhiteSpace(configurationPath))
        {
            configurationDirectoryPath = Path.GetDirectoryName(configurationPath);
        }

        configurationDirectoryPath ??= AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(configurationDirectoryPath, trimmed));
    }

    public static string? ResolveProfileName(WifiSettings wifiSettings, string? configurationPath)
    {
        if (wifiSettings.HasEnterpriseProfile)
        {
            string? profilePath = ResolveAssetPath(wifiSettings.EnterpriseProfileTemplatePath, configurationPath);
            return TryReadProfileName(profilePath);
        }

        return string.IsNullOrWhiteSpace(wifiSettings.Ssid)
            ? null
            : wifiSettings.Ssid.Trim();
    }

    public static string? TryReadProfileName(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        string fileContents = File.ReadAllText(profilePath);
        const string openTag = "<name>";
        const string closeTag = "</name>";
        int startIndex = fileContents.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        int endIndex = fileContents.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        int contentStart = startIndex + openTag.Length;
        string profileName = fileContents[contentStart..endIndex].Trim();
        return string.IsNullOrWhiteSpace(profileName)
            ? null
            : profileName;
    }
}
