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

    /// <summary>
    /// Removes temporary folders left by pre-OOBE provisioning scripts.
    /// </summary>
    public const string CleanupPreOobe = "Foundry.Deploy.PreOobe.Cleanup-PreOobe.ps1";
}
