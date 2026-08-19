// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationOverviewEvaluatorTests
{
    [Fact]
    public void Evaluate_DefaultConfiguration_UsesValidDefaultsAndNeutralOptionalStates()
    {
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(new FoundryConfigurationDocument()));

        Assert.Equal(ConfigurationOverviewState.Default, evaluation[ConfigurationOverviewItem.Architecture]);
        Assert.Equal(ConfigurationOverviewState.Default, evaluation[ConfigurationOverviewItem.SecureBoot]);
        Assert.Equal(ConfigurationOverviewState.Default, evaluation[ConfigurationOverviewItem.TimeZone]);
        Assert.Equal(ConfigurationOverviewState.Default, evaluation[ConfigurationOverviewItem.DeploymentCompletion]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.DriverOptions]);
        Assert.Equal(ConfigurationOverviewState.NotConfigured, evaluation[ConfigurationOverviewItem.EthernetDot1x]);
        Assert.Equal(ConfigurationOverviewState.NotConfigured, evaluation[ConfigurationOverviewItem.Wifi]);
        Assert.Equal(ConfigurationOverviewState.NotSelected, evaluation[ConfigurationOverviewItem.AutopilotJsonProfile]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.OperatingSystemSelection]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.MachineNaming]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.Oobe]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.OptionalFeatures]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.AppxRemoval]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.AiComponents]);
    }

    [Fact]
    public void Evaluate_EnabledConfigurationWithoutRequiredRuntimeInputs_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings
            {
                DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true },
                CustomDriverDirectoryPath = "C:\\MissingDrivers"
            },
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "Contoso",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with
            {
                IsDeploymentProtectionSecretReady = false,
                IsCustomDriverConfigurationReady = false
            });

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.DriverOptions]);
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.Wifi]);
    }

    [Fact]
    public void Evaluate_Autopilot_OnlyActiveModeCanBeConfiguredOrNeedAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload
            }
        };

        ConfigurationOverviewEvaluation ready = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsAutopilotConfigurationReady = true });
        ConfigurationOverviewEvaluation blocked = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsAutopilotConfigurationReady = false });

        Assert.Equal(ConfigurationOverviewState.NotSelected, ready[ConfigurationOverviewItem.AutopilotJsonProfile]);
        Assert.Equal(ConfigurationOverviewState.Configured, ready[ConfigurationOverviewItem.AutopilotZeroTouch]);
        Assert.Equal(ConfigurationOverviewState.NotSelected, ready[ConfigurationOverviewItem.AutopilotInteractive]);
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, blocked[ConfigurationOverviewItem.AutopilotZeroTouch]);
    }

    [Fact]
    public void Evaluate_OptionalCustomizationWithoutActions_IsEffectivelyDisabled()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                WindowsOptionalFeatures = new WindowsOptionalFeatureSettings { IsEnabled = true },
                AppxRemoval = new AppxRemovalSettings { IsEnabled = true },
                AiComponentRemoval = new AiComponentRemovalSettings { IsEnabled = true }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.OptionalFeatures]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.AppxRemoval]);
        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.AiComponents]);
    }

    [Fact]
    public void Evaluate_EnabledOpenOsSelectionAndProvisionedWifi_AreConfigured()
    {
        var configuration = new FoundryConfigurationDocument
        {
            OperatingSystemSelection = new OperatingSystemSelectionSettings { IsEnabled = true },
            Network = new NetworkSettings { WifiProvisioned = true }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.Configured, evaluation[ConfigurationOverviewItem.OperatingSystemSelection]);
        Assert.Equal(ConfigurationOverviewState.Configured, evaluation[ConfigurationOverviewItem.Wifi]);
    }

    [Fact]
    public void Evaluate_InvalidMachineNamePrefix_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Prefix = "INVALID_PREFIX"
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.MachineNaming]);
    }

    [Fact]
    public void Count_InvalidEthernetConfiguration_CountsOneActionableItem()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(1, evaluation.Count(ConfigurationOverviewState.NeedsAttention));
    }

    [Fact]
    public void EvaluateTarget_InvalidEthernetConfiguration_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true }
            }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        ConfigurationOverviewState state = ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation,
            ConfigurationNavigationTarget.EthernetDot1x);

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, state);
    }

    [Fact]
    public void EvaluateTarget_GeneralConfigurationWithInvalidSecret_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings
            {
                DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true }
            }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsDeploymentProtectionSecretReady = false });

        ConfigurationOverviewState state = ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation,
            ConfigurationNavigationTarget.General);

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, state);
    }

    private static ConfigurationOverviewContext CreateContext(FoundryConfigurationDocument configuration)
    {
        return new ConfigurationOverviewContext
        {
            Configuration = configuration,
            EffectiveNetwork = configuration.Network,
            IsWinPeLanguageReady = true,
            IsCustomDriverConfigurationReady = true,
            IsDeploymentProtectionSecretReady = true,
            IsAutopilotConfigurationReady = true
        };
    }
}
