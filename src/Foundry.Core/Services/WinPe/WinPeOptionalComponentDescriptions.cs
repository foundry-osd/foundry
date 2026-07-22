// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Provides short human-readable descriptions for the well-known WinPE optional components so the UI can show
/// a tooltip explaining what each component adds.
/// </summary>
public static class WinPeOptionalComponentDescriptions
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WinPE-WMI"] = "Windows Management Instrumentation (WMI) support.",
            ["WinPE-NetFX"] = ".NET Framework support (required by PowerShell).",
            ["WinPE-Scripting"] = "Windows Script Host (VBScript/JScript) support.",
            ["WinPE-PowerShell"] = "Windows PowerShell support.",
            ["WinPE-DismCmdlets"] = "DISM PowerShell cmdlets.",
            ["WinPE-SecureStartup"] = "BitLocker and TPM support.",
            ["WinPE-SecureBootCmdlets"] = "Secure Boot PowerShell cmdlets.",
            ["WinPE-StorageWMI"] = "Storage management PowerShell cmdlets.",
            ["WinPE-EnhancedStorage"] = "Enhanced storage support (IEEE 1667 and encrypted drives).",
            ["WinPE-Dot3Svc"] = "Wired 802.1X authentication support.",
            ["WinPE-WinReCfg"] = "Windows Recovery Environment configuration support.",
            ["WinPE-WiFi-Package"] = "Wi-Fi networking support.",
            ["WinPE-PPPoE"] = "PPPoE dial-up networking support.",
            ["WinPE-RNDIS"] = "Remote NDIS (USB tethering) networking support.",
            ["WinPE-MDAC"] = "Microsoft Data Access Components (SQL connectivity).",
            ["WinPE-HTA"] = "HTML Application (HTA) support.",
            ["WinPE-FMAPI"] = "File Management API for offline file recovery.",
            ["WinPE-WDS-Tools"] = "Windows Deployment Services (WDS) client tools.",
            ["WinPE-Setup"] = "Windows Setup support.",
            ["WinPE-Setup-Client"] = "Windows Setup (client editions) support.",
            ["WinPE-Setup-Server"] = "Windows Setup (server editions) support.",
            ["WinPE-LegacySetup"] = "Legacy Windows Setup media support.",
            ["WinPE-Fonts-Legacy"] = "Legacy font support.",
            ["WinPE-FontSupport-WinRE"] = "WinRE font support.",
            ["WinPE-GamingPeripherals"] = "Gaming peripheral (controller) support.",
            ["WinPE-PlatformId"] = "Platform identifier support.",
            ["WinPE-PmemCmdlets"] = "Persistent memory PowerShell cmdlets.",
            ["WinPE-Dot3Svc-Package"] = "Wired 802.1X authentication support.",
        };

    /// <summary>
    /// Gets the description for a WinPE optional component, or an empty string when none is known.
    /// </summary>
    public static string GetDescription(string componentName)
    {
        return Descriptions.TryGetValue(componentName, out string? description) ? description : string.Empty;
    }
}
