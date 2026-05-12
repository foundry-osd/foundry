namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Defines manifest resource names for embedded pre-OOBE PowerShell scripts.
/// </summary>
public static class PreOobeScriptResources
{
    /// <summary>
    /// Installs a deferred driver package during the first full Windows boot.
    /// </summary>
    public const string InstallDriverPack = "Foundry.Deploy.PreOobe.Install-DriverPack.ps1";
}
