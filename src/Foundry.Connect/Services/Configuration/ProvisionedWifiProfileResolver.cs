using System.IO;
using System.Xml.Linq;
using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Configuration;

internal static class ProvisionedWifiProfileResolver
{
    private static readonly XNamespace WlanProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";

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

    public static string? TryReadProfileAuthentication(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        try
        {
            XDocument document = XDocument.Load(profilePath);
            return document
                .Descendants(WlanProfileNamespace + "authentication")
                .Select(static element => element.Value?.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }
        catch
        {
            return null;
        }
    }
}
