// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class NavigationConfigurationStatusEvaluator
{
    public static bool IsConfigured(
        FoundryConfigurationDocument configuration,
        ConfigurationNavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        CustomizationSettings customization = configuration.Customization;
        return target switch
        {
            ConfigurationNavigationTarget.AutopilotJsonProfile => IsActiveAutopilotMode(
                configuration.Autopilot,
                AutopilotProvisioningMode.JsonProfile),
            ConfigurationNavigationTarget.AutopilotHardwareHashUpload => IsActiveAutopilotMode(
                configuration.Autopilot,
                AutopilotProvisioningMode.HardwareHashUpload),
            ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload => IsActiveAutopilotMode(
                configuration.Autopilot,
                AutopilotProvisioningMode.InteractiveHardwareHashUpload),
            ConfigurationNavigationTarget.OperatingSystemSelection => configuration.OperatingSystemSelection.IsEnabled,
            ConfigurationNavigationTarget.MachineNaming => customization.MachineNaming.IsEnabled &&
                (string.IsNullOrWhiteSpace(customization.MachineNaming.Prefix) ||
                 ComputerNameRules.IsValid(customization.MachineNaming.Prefix)),
            ConfigurationNavigationTarget.Oobe => customization.Oobe.IsEnabled,
            ConfigurationNavigationTarget.WindowsOptionalFeatures => customization.WindowsOptionalFeatures.IsEnabled &&
                (customization.WindowsOptionalFeatures.EnabledFeatureIds.Count > 0 ||
                 customization.WindowsOptionalFeatures.DisabledFeatureIds.Count > 0),
            ConfigurationNavigationTarget.AppxRemoval => customization.AppxRemoval.IsEnabled &&
                customization.AppxRemoval.PackageNames.Count > 0,
            ConfigurationNavigationTarget.AiComponentRemoval => customization.AiComponentRemoval.IsEnabled &&
                customization.AiComponentRemoval.HasAnyAction(),
            _ => false
        };
    }

    private static bool IsActiveAutopilotMode(AutopilotSettings settings, AutopilotProvisioningMode mode) =>
        settings.IsEnabled && settings.ProvisioningMode == mode;

}
