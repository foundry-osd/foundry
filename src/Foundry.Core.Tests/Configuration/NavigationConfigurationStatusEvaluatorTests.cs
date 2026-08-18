// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class NavigationConfigurationStatusEvaluatorTests
{
    [Fact]
    public void IsConfigured_AutopilotBadgeIsExclusiveToActiveMode()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload
            }
        };

        Assert.True(NavigationConfigurationStatusEvaluator.IsConfigured(
            configuration,
            ConfigurationNavigationTarget.AutopilotHardwareHashUpload));
        Assert.False(NavigationConfigurationStatusEvaluator.IsConfigured(
            configuration,
            ConfigurationNavigationTarget.AutopilotJsonProfile));
        Assert.False(NavigationConfigurationStatusEvaluator.IsConfigured(
            configuration,
            ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload));
    }

    [Fact]
    public void IsConfigured_OptionalFeaturesRequiresAtLeastOneAction()
    {
        var emptySelection = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                WindowsOptionalFeatures = new WindowsOptionalFeatureSettings { IsEnabled = true }
            }
        };
        var configured = emptySelection with
        {
            Customization = emptySelection.Customization with
            {
                WindowsOptionalFeatures = new WindowsOptionalFeatureSettings
                {
                    IsEnabled = true,
                    EnabledFeatureIds = ["NetFx3"]
                }
            }
        };

        Assert.False(NavigationConfigurationStatusEvaluator.IsConfigured(
            emptySelection,
            ConfigurationNavigationTarget.WindowsOptionalFeatures));
        Assert.True(NavigationConfigurationStatusEvaluator.IsConfigured(
            configured,
            ConfigurationNavigationTarget.WindowsOptionalFeatures));
    }
}
