namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Stages the interactive Autopilot registration assistant into an applied Windows image.
/// </summary>
public interface IAutopilotInteractiveRegistrationProvisioningService
{
    /// <summary>
    /// Stages the assistant files, OOBE launch hook, and returns their offline paths.
    /// </summary>
    /// <param name="targetWindowsPartitionRoot">Root of the applied Windows partition.</param>
    /// <returns>Provisioned assistant paths.</returns>
    AutopilotInteractiveRegistrationProvisioningResult Provision(string targetWindowsPartitionRoot);
}
