// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Defines manifest resource names for embedded pre-OOBE PowerShell scripts.
/// </summary>
public static class PreOobeScriptResources
{
    /// <summary>Executes only explicit first-boot actions and publishes their journal.</summary>
    public const string Runner = "Foundry.Deploy.PreOobe.Invoke-FoundryPreOobe.ps1";

    /// <summary>Contains isolated testable first-boot execution and cleanup functions.</summary>
    public const string Functions = "Foundry.Deploy.PreOobe.Foundry-PreOobeFunctions.ps1";
    /// <summary>
    /// Installs a deferred driver package during the first full Windows boot.
    /// </summary>
    public const string InstallDriverPack = "Foundry.Deploy.PreOobe.Install-DriverPack.ps1";

    /// <summary>
    /// Removes selected provisioned AppX packages before OOBE creates user profiles.
    /// </summary>
    public const string RemoveAppx = "Foundry.Deploy.PreOobe.Remove-AppX.ps1";

    /// <summary>
    /// Removes selected AI provisioned AppX packages before OOBE creates user profiles.
    /// </summary>
    public const string RemoveAiComponents = "Foundry.Deploy.PreOobe.Remove-AiComponents.ps1";

    /// <summary>
    /// Imports captured network profiles and certificate material before OOBE starts.
    /// </summary>
    public const string ImportNetworkProfiles = "Foundry.Deploy.PreOobe.Import-NetworkProfiles.ps1";

    /// <summary>
    /// Removes temporary folders left by pre-OOBE provisioning scripts.
    /// </summary>
    public const string CleanupPreOobe = "Foundry.Deploy.PreOobe.Cleanup-PreOobe.ps1";
}
