// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Pairs an invariant execution identity with an independently localized display label.</summary>
public sealed record DeploymentPlanEntry(string Name, string Label);

/// <summary>Records an actual terminal step outcome without treating skipped work as successful execution.</summary>
public sealed record DeploymentStepOutcome(string Name, DeploymentStepState State, string Message);

/// <summary>
/// Builds the remaining deployment workflow from effective selections and resolved runtime evidence.
/// Completed entries retain their order and identity when later payload discovery removes future work.
/// </summary>
public static class DeploymentPlan
{
    /// <summary>Builds a request preview or refines a running plan without inspecting files or changing the target.</summary>
    public static IReadOnlyList<DeploymentPlanEntry> Build(
        DeploymentContext request,
        DeploymentRuntimeState? state = null,
        bool? usesTargetStorage = null,
        bool networkResolved = false,
        bool hasNetworkPayload = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<string> names = [];
        if (request.UsesCustomUnattend) names.Add(DeploymentStepNames.ValidateCustomUnattend);
        names.Add(DeploymentStepNames.ValidateTargetConfiguration);

        bool external = usesTargetStorage == false || request.OperatingSystem is CustomImageSelection;
        if (!external) names.Add(DeploymentStepNames.PrepareTargetDiskLayout);
        names.Add(DeploymentStepNames.DownloadOperatingSystemImage);
        names.Add(DeploymentStepNames.CheckWindowsImage);
        if (external) names.Add(DeploymentStepNames.PrepareTargetDiskLayout);
        names.Add(DeploymentStepNames.ApplyOperatingSystemImage);
        names.Add(DeploymentStepNames.ConfigureWindowsBoot);
        if (request.UsesCustomUnattend) names.Add(DeploymentStepNames.StageCustomUnattend);

        DriverPackInstallMode driverMode = ResolveDriverMode(request);
        bool driverLookupFinished = HasOutcome(state, DeploymentStepNames.DownloadDriverPack);
        bool hasDrivers = request.DriverPackSelectionKind != DriverPackSelectionKind.None &&
            !(driverLookupFinished && request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog &&
              state!.MicrosoftUpdateCatalogDriverPaths.Count == 0);
        if (request.DriverPackSelectionKind != DriverPackSelectionKind.None) names.Add(DeploymentStepNames.DownloadDriverPack);
        if (hasDrivers && driverMode == DriverPackInstallMode.OfflineInf)
        {
            names.Add(DeploymentStepNames.ExtractDriverPack);
            names.Add(DeploymentStepNames.ApplyDriverPack);
        }
        if (hasDrivers && driverMode == DriverPackInstallMode.DeferredSetupComplete) names.Add(DeploymentStepNames.StageDriverInstaller);

        // Battery/identifier restrictions remain visible as a reason when firmware was requested.
        bool firmwareRequested = request.ApplyFirmwareUpdates && state?.HardwareProfile?.IsVirtualMachine != true;
        bool firmwareFound = !HasOutcome(state, DeploymentStepNames.DownloadFirmwareUpdate) ||
            !string.IsNullOrWhiteSpace(state?.DownloadedFirmwarePath);
        if (firmwareRequested)
        {
            names.Add(DeploymentStepNames.DownloadFirmwareUpdate);
            if (firmwareFound)
            {
                names.Add(DeploymentStepNames.ExtractFirmwareUpdate);
                names.Add(DeploymentStepNames.ApplyFirmwareUpdate);
            }
        }

        if (!request.UsesCustomUnattend) names.Add(DeploymentStepNames.ConfigureTargetComputerName);
        if (!request.UsesCustomUnattend && request.Oobe.IsEnabled) names.Add(DeploymentStepNames.ConfigureOobeSettings);
        if (ConfigureAiPoliciesStep.IsRequired(request.AiComponentRemoval)) names.Add(DeploymentStepNames.ConfigureAiPolicies);
        if (request.WindowsOptionalFeatures.IsEnabled && request.WindowsOptionalFeatures.Actions.Count > 0)
            names.Add(DeploymentStepNames.ConfigureWindowsOptionalFeatures);

        bool setupTasks = PreOobeScriptDefinitionBuilder.HasScripts(
            request.AppxRemoval,
            request.AiComponentRemoval,
            hasDrivers && driverMode == DriverPackInstallMode.DeferredSetupComplete,
            hasNetworkPayload || (!networkResolved && request.Network.ProfileRoaming.IsAnyEnabled),
            StagePreOobeCustomizationStep.ShouldActivateWindowsOem(request));
        if (setupTasks) names.Add(DeploymentStepNames.StagePreOobeCustomization);
        names.Add(DeploymentStepNames.ConfigureRecoveryEnvironment);
        if (hasDrivers && driverMode == DriverPackInstallMode.OfflineInf) names.Add(DeploymentStepNames.ApplyRecoveryDrivers);
        names.Add(DeploymentStepNames.SealRecoveryPartition);
        if (request.IsAutopilotEnabled) names.Add(DeploymentStepNames.ProvisionAutopilot);
        names.Add(DeploymentStepNames.FinalizeDeploymentAndWriteLogs);

        string[] finished = state?.StepOutcomes.Select(outcome => outcome.Name).ToArray() ?? [];
        return finished.Concat(names.Where(name => !finished.Contains(name, StringComparer.Ordinal)))
            .Select(name => new DeploymentPlanEntry(name, ResolveLabel(name, request)))
            .ToArray();
    }

    /// <summary>Resolves the package strategy before extraction is conditionally removed from the workflow.</summary>
    internal static DriverPackInstallMode ResolveDriverMode(DeploymentContext request)
    {
        if (request.DriverPackSelectionKind == DriverPackSelectionKind.None) return DriverPackInstallMode.None;
        if (request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog) return DriverPackInstallMode.OfflineInf;
        if (request.DriverPack is null) return DriverPackInstallMode.OfflineInf;
        string fileName = DeploymentStepExecutionContext.ResolveFileName(request.DriverPack.FileName, request.DriverPack.DownloadUrl);
        try
        {
            return new DriverPackStrategyResolver().Resolve(request.DriverPackSelectionKind, request.DriverPack, fileName).InstallMode;
        }
        catch (InvalidOperationException)
        {
            // Keep malformed requested packages visible; normal step validation reports the failure.
            return DriverPackInstallMode.OfflineInf;
        }
    }

    private static bool HasOutcome(DeploymentRuntimeState? state, string name) =>
        state?.StepOutcomes.Any(outcome => outcome.Name == name) == true;

    private static string ResolveLabel(string name, DeploymentContext request) =>
        name == DeploymentStepNames.DownloadOperatingSystemImage && request.OperatingSystem is CustomImageSelection
            ? "Resolve custom image"
            :
        name == DeploymentStepNames.ProvisionAutopilot
            ? request.AutopilotProvisioningMode switch
            {
                AutopilotProvisioningMode.HardwareHashUpload => "Register Autopilot device",
                AutopilotProvisioningMode.InteractiveHardwareHashUpload => "Prepare Autopilot assistant",
                _ => "Copy Autopilot profile"
            }
            : name;
}
