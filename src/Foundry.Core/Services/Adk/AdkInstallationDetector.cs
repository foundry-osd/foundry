// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

public sealed class AdkInstallationDetector(IAdkInstallationProbe probe)
{
    public const string DeploymentToolsRelativePath = @"Assessment and Deployment Kit\Deployment Tools";
    public const string WinPeRelativePath = @"Assessment and Deployment Kit\Windows Preinstallation Environment";

    /// <summary>The known update used for advisory servicing verification, independent of base-version compatibility.</summary>
    public const string RecommendedServicingUpdate = "KB5101684";
    private const string RequiredVersionPolicyText = "Windows ADK 24H2 / 10.1.26100.2454";
    private static readonly Version SupportedWindows11AdkBuild = new(10, 1, 26100, 2454);
    private static readonly string[] WinPeComponents =
    [
        "Windows PE wims (DesktopEditions)", "Windows PE Optional Packages (DesktopEditions)",
        "Windows PE Scripts", "Windows PE Boot Files (DesktopEditions)"
    ];

    public AdkInstallationStatus Detect()
    {
        string? kitsRootPath = probe.GetKitsRootPath();
        bool hasKitsRoot = !string.IsNullOrWhiteSpace(kitsRootPath);
        bool hasDeploymentTools = hasKitsRoot
            && probe.DirectoryExists(Path.Combine(kitsRootPath!, DeploymentToolsRelativePath));
        bool hasWinPeAddon = hasKitsRoot && HasUsableWinPeAddon(kitsRootPath!);
        IReadOnlyList<AdkInstalledProduct> products = probe.GetInstalledProducts();
        string? installedVersion = ResolveInstalledVersion(products);
        bool isInstalled = hasDeploymentTools;
        AdkVersionRelation versionRelation = GetVersionRelation(installedVersion);
        bool isCompatible = isInstalled && versionRelation == AdkVersionRelation.Supported;
        bool winPeCompatible = hasWinPeAddon && Version.TryParse(installedVersion, out Version? adkVersion)
            && WinPeComponents.All(name => HasMatchingComponent(products, name, adkVersion));
        AdkServicingState servicing = isCompatible ? probe.GetServicingState() : AdkServicingState.Unknown;

        return new(
            isInstalled,
            isCompatible,
            hasWinPeAddon,
            installedVersion,
            versionRelation,
            kitsRootPath,
            RequiredVersionPolicyText,
            servicing,
            winPeCompatible,
            winPeCompatible && HasArchitectureAssets(kitsRootPath!, "amd64"),
            winPeCompatible && HasArchitectureAssets(kitsRootPath!, "arm64"),
            products.Any(product => WinPeComponents.Contains(product.DisplayName, StringComparer.OrdinalIgnoreCase)));
    }

    public static AdkVersionRelation GetVersionRelation(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText) || !Version.TryParse(versionText, out Version? version))
        {
            return AdkVersionRelation.Unknown;
        }

        int majorComparison = version.Major.CompareTo(SupportedWindows11AdkBuild.Major);
        if (majorComparison != 0)
        {
            return majorComparison < 0 ? AdkVersionRelation.BelowSupported : AdkVersionRelation.AboveSupported;
        }

        int minorComparison = version.Minor.CompareTo(SupportedWindows11AdkBuild.Minor);
        if (minorComparison != 0)
        {
            return minorComparison < 0 ? AdkVersionRelation.BelowSupported : AdkVersionRelation.AboveSupported;
        }

        int buildComparison = version.Build.CompareTo(SupportedWindows11AdkBuild.Build);
        if (buildComparison != 0)
        {
            return buildComparison < 0 ? AdkVersionRelation.BelowSupported : AdkVersionRelation.AboveSupported;
        }

        return version < SupportedWindows11AdkBuild ? AdkVersionRelation.BelowSupported : AdkVersionRelation.Supported;
    }

    private static bool HasMatchingComponent(IReadOnlyList<AdkInstalledProduct> products, string name, Version version)
    {
        AdkInstalledProduct[] matches = products.Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length > 0 && matches.All(p => Version.TryParse(p.DisplayVersion, out Version? detected) && detected == version);
    }

    private bool HasArchitectureAssets(string kitsRootPath, string architecture)
    {
        string winPeRoot = Path.Combine(kitsRootPath, WinPeRelativePath, architecture);
        string bootRoot = Path.Combine(kitsRootPath, DeploymentToolsRelativePath, architecture, "Oscdimg");
        // copype copies all four boot variants regardless of the selected signing mode.
        string[] bootFiles = ["efisys.bin", "efisys_noprompt.bin", "efisys_EX.bin", "efisys_noprompt_EX.bin"];
        return probe.FileExists(Path.Combine(winPeRoot, "en-us", "winpe.wim"))
            && probe.FileExists(Path.Combine(winPeRoot, "WinPE_OCs", "WinPE-SecureStartup.cab"))
            && probe.FileExists(Path.Combine(winPeRoot, "Media", "Boot", "boot.sdi"))
            && probe.FileExists(Path.Combine(winPeRoot, "Media", "EFI", "Microsoft", "Boot", "BCD"))
            && bootFiles.All(file => probe.FileExists(Path.Combine(bootRoot, file)));
    }

    private bool HasUsableWinPeAddon(string kitsRootPath)
    {
        string winPeRootPath = Path.Combine(kitsRootPath, WinPeRelativePath);
        if (!probe.DirectoryExists(winPeRootPath))
        {
            return false;
        }

        return ProbeFileInTree(winPeRootPath, "copype.cmd")
            && ProbeFileInTree(winPeRootPath, "MakeWinPEMedia.cmd");
    }

    private bool ProbeFileInTree(string directoryPath, string fileName)
    {
        string directPath = Path.Combine(directoryPath, fileName);
        return probe.FileExists(directPath) || probe.DirectoryContainsFile(directoryPath, fileName);
    }

    private static string? ResolveInstalledVersion(IReadOnlyList<AdkInstalledProduct> products)
    {
        string? strictAdkVersion = products
            .Where(product => string.Equals(
                product.DisplayName,
                "Windows Assessment and Deployment Kit",
                StringComparison.OrdinalIgnoreCase))
            .Select(product => product.DisplayVersion)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));

        if (!string.IsNullOrWhiteSpace(strictAdkVersion))
        {
            return strictAdkVersion;
        }

        return products
            .Where(product => product.DisplayName.Equals("Windows Deployment Tools", StringComparison.OrdinalIgnoreCase))
            .Select(product => product.DisplayVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .GroupBy(version => version!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => ParseVersionOrDefault(group.Key))
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static Version ParseVersionOrDefault(string versionText)
    {
        return Version.TryParse(versionText, out Version? version) ? version : new Version();
    }

}
