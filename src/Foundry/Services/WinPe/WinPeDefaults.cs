using System.Reflection;
using System.Text;

namespace Foundry.Services.WinPe;

internal static class WinPeDefaults
{
    private static readonly Lazy<string> BootstrapScriptContent = new(LoadDefaultBootstrapScriptContent);

    public const string DefaultUnifiedCatalogUri = "https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/WinPE/WinPE_Unified.xml";
    public const string DefaultStartnetPathInImage = @"Windows\System32\startnet.cmd";
    public const string DefaultBootstrapScriptFileName = "FoundryBootstrap.ps1";
    public const string DefaultBootstrapInvocation = @"powershell.exe -ExecutionPolicy Bypass -NoProfile -File X:\Windows\System32\FoundryBootstrap.ps1";
    public const string DefaultBootstrapScriptResourceName = "Foundry.WinPe.BootstrapScript";
    public const string EmbeddedDeployArchivePathInImage = @"ProgramData\Foundry\Deploy\Seed\Foundry.Deploy.zip";
    public const string LocalDeployEnableEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY";
    public const string LocalDeployArchiveEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE";
    public const string LocalDeployProjectEnvironmentVariable = "FOUNDRY_WINPE_LOCAL_DEPLOY_PROJECT";

    public static string GetDefaultBootstrapScriptContent()
    {
        return BootstrapScriptContent.Value;
    }

    private static string LoadDefaultBootstrapScriptContent()
    {
        Assembly assembly = typeof(WinPeDefaults).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(DefaultBootstrapScriptResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded WinPE bootstrap script resource '{DefaultBootstrapScriptResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
