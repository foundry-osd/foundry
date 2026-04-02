using System.Reflection;
using System.Text;

namespace Foundry.Services.WinPe;

internal static class WinPeDefaults
{
    private static readonly Lazy<string> BootstrapScriptContent = new(LoadDefaultBootstrapScriptContent);
    private static readonly Lazy<string> IanaWindowsTimeZoneMapContent = new(LoadIanaWindowsTimeZoneMapContent);

    public const string DefaultUnifiedCatalogUri = "https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/WinPE/WinPE_Unified.xml";
    public const string DefaultOperatingSystemCatalogUri = "https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/OS/OperatingSystem.xml";
    public const string DefaultStartnetPathInImage = @"Windows\System32\startnet.cmd";
    public const string DefaultBootstrapScriptFileName = "FoundryBootstrap.ps1";
    public const string DefaultBootstrapInvocation = @"powershell.exe -ExecutionPolicy Bypass -NoProfile -File X:\Windows\System32\FoundryBootstrap.ps1";
    public const string DefaultBootstrapScriptResourceName = "Foundry.WinPe.BootstrapScript";
    public const string DefaultTimeZoneMapResourceName = "Foundry.Configuration.IanaWindowsTimeZones";
    public const string EmbeddedConnectArchivePathInImage = @"Foundry\Seed\Foundry.Connect.zip";
    public const string EmbeddedDeployArchivePathInImage = @"Foundry\Seed\Foundry.Deploy.zip";
    public const string EmbeddedConnectConfigPathInImage = @"Foundry\Config\foundry.connect.config.json";
    public const string EmbeddedDeployConfigPathInImage = @"Foundry\Config\foundry.deploy.config.json";
    public const string EmbeddedNetworkAssetsPathInImage = @"Foundry\Config\Network";
    public const string EmbeddedTimeZoneMapPathInImage = @"Foundry\Config\iana-windows-timezones.json";
    public const string EmbeddedAutopilotProfilesPathInImage = @"Foundry\Config\Autopilot";
    public const string EmbeddedSevenZipToolsPathInImage = @"Foundry\Tools\7zip";
    public const string BundledSevenZipRelativePath = @"Assets\7z";
    public const string LocalConnectEnableEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_CONNECT";
    public const string LocalDeployEnableEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY";
    public const string LocalConnectArchiveEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_CONNECT_ARCHIVE";
    public const string LocalConnectProjectEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_CONNECT_PROJECT";
    public const string LocalDeployArchiveEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE";
    public const string LocalDeployProjectEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY_PROJECT";

    public static string GetProgramDataRootPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Foundry");
    }

    public static string GetWinPeWorkspaceRootPath()
    {
        return Path.Combine(GetProgramDataRootPath(), "WinPeWorkspace");
    }

    public static string GetUsbQueryWorkingDirectoryPath()
    {
        return Path.Combine(GetProgramDataRootPath(), "UsbQuery");
    }

    public static string GetInstallerCacheDirectoryPath()
    {
        return Path.Combine(GetProgramDataRootPath(), "Installers");
    }

    public static string GetIsoWorkspaceRootPath()
    {
        return Path.Combine(GetProgramDataRootPath(), "IsoWorkspace");
    }

    public static string GetIsoOutputTempRootPath()
    {
        return Path.Combine(GetProgramDataRootPath(), "IsoOutputTemp");
    }

    public static string GetDefaultBootstrapScriptContent()
    {
        return BootstrapScriptContent.Value;
    }

    public static string GetIanaWindowsTimeZoneMapContent()
    {
        return IanaWindowsTimeZoneMapContent.Value;
    }

    private static string LoadDefaultBootstrapScriptContent()
    {
        return LoadEmbeddedText(DefaultBootstrapScriptResourceName);
    }

    private static string LoadIanaWindowsTimeZoneMapContent()
    {
        return LoadEmbeddedText(DefaultTimeZoneMapResourceName);
    }

    private static string LoadEmbeddedText(string resourceName)
    {
        Assembly assembly = typeof(WinPeDefaults).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
