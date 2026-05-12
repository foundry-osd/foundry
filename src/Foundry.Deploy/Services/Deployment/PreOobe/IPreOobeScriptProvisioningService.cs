namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Stages embedded PowerShell scripts and wires SetupComplete.cmd to run them before OOBE.
/// </summary>
public interface IPreOobeScriptProvisioningService
{
    /// <summary>
    /// Stages the requested scripts into the offline Windows image and creates the SetupComplete.cmd launcher hook.
    /// </summary>
    /// <param name="targetWindowsPartitionRoot">Offline root of the target Windows partition.</param>
    /// <param name="scripts">Script definitions selected by the deployment workflow.</param>
    /// <returns>The paths written during provisioning.</returns>
    PreOobeScriptProvisioningResult Provision(
        string targetWindowsPartitionRoot,
        IEnumerable<PreOobeScriptDefinition> scripts);
}
