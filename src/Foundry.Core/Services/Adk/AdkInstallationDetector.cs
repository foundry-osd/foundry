namespace Foundry.Core.Services.Adk;

public sealed class AdkInstallationDetector(IAdkInstallationProbe probe)
{
    public const string DeploymentToolsRelativePath = @"Assessment and Deployment Kit\Deployment Tools";
    public const string WinPeRelativePath = @"Assessment and Deployment Kit\Windows Preinstallation Environment";

    private const string RequiredVersionPolicyText = "Windows ADK 10.1.26100.2454+ with the latest ADK servicing patch";
    private static readonly Version MinimumWindows11AdkVersion = new(10, 1, 26100, 2454);

    public AdkInstallationStatus Detect()
    {
        string? kitsRootPath = probe.GetKitsRootPath();
        bool hasKitsRoot = !string.IsNullOrWhiteSpace(kitsRootPath);
        bool hasDeploymentTools = hasKitsRoot
            && probe.DirectoryExists(Path.Combine(kitsRootPath!, DeploymentToolsRelativePath));
        bool hasWinPeAddon = hasKitsRoot
            && probe.DirectoryExists(Path.Combine(kitsRootPath!, WinPeRelativePath));
        string? installedVersion = ResolveInstalledVersion(probe.GetInstalledProducts());
        bool isInstalled = hasDeploymentTools;
        bool isCompatible = isInstalled && IsCompatibleVersion(installedVersion);

        return new(
            isInstalled,
            isCompatible,
            hasWinPeAddon,
            installedVersion,
            kitsRootPath,
            RequiredVersionPolicyText);
    }

    public static bool IsCompatibleVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        if (Version.TryParse(versionText, out Version? version))
        {
            return IsVersionAtLeast(version, MinimumWindows11AdkVersion);
        }

        return false;
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
            .Where(IsAdkComponentProduct)
            .Select(product => product.DisplayVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .GroupBy(version => version!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => ParseVersionOrDefault(group.Key))
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static bool IsAdkComponentProduct(AdkInstalledProduct product)
    {
        return product.DisplayName.Equals("Windows Deployment Tools", StringComparison.OrdinalIgnoreCase)
            || product.DisplayName.Contains("WinPE", StringComparison.OrdinalIgnoreCase)
            || (product.DisplayName.Contains("Windows PE", StringComparison.OrdinalIgnoreCase)
                && (product.DisplayName.Contains("Deployment", StringComparison.OrdinalIgnoreCase)
                    || product.DisplayName.Contains("deploiement", StringComparison.OrdinalIgnoreCase)));
    }

    private static Version ParseVersionOrDefault(string versionText)
    {
        return Version.TryParse(versionText, out Version? version) ? version : new Version();
    }

    private static bool IsVersionAtLeast(Version version, Version minimumVersion)
    {
        return version.Major == minimumVersion.Major
            && version.Minor == minimumVersion.Minor
            && version.Build == minimumVersion.Build
            && version.Revision >= minimumVersion.Revision;
    }
}
