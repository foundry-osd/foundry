// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
    /// <param name="assignedComputerName">Final deployment name to assign in Autopilot, or null to preserve its existing name.</param>
    /// <returns>Provisioned assistant paths.</returns>
    AutopilotInteractiveRegistrationProvisioningResult Provision(string targetWindowsPartitionRoot, string? assignedComputerName = null);
}
