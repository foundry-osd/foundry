// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationOverviewEvaluatorTests
{
    [Theory]
    [InlineData(DomainJoinMode.Interactive, true, ConfigurationOverviewState.Configured)]
    [InlineData(DomainJoinMode.Interactive, false, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(DomainJoinMode.Automatic, true, ConfigurationOverviewState.Configured)]
    [InlineData(DomainJoinMode.Automatic, false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_DomainJoinOnlyActiveModeUsesReadiness(DomainJoinMode mode, bool ready, ConfigurationOverviewState expected)
    {
        var configuration = new FoundryConfigurationDocument
        {
            DomainJoin = new DomainJoinSettings { IsEnabled = true, Mode = mode }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsDomainJoinConfigurationReady = ready });
        ConfigurationOverviewItem active = mode == DomainJoinMode.Interactive ? ConfigurationOverviewItem.DomainJoinInteractive : ConfigurationOverviewItem.DomainJoinAutomatic;
        ConfigurationOverviewItem inactive = mode == DomainJoinMode.Interactive ? ConfigurationOverviewItem.DomainJoinAutomatic : ConfigurationOverviewItem.DomainJoinInteractive;
        Assert.Equal(expected, evaluation[active]);
        Assert.Equal(ConfigurationOverviewState.NotSelected, evaluation[inactive]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(evaluation,
            mode == DomainJoinMode.Interactive ? ConfigurationNavigationTarget.DomainJoinInteractive : ConfigurationNavigationTarget.DomainJoinAutomatic));
    }

    [Fact]
    public void Evaluate_NetworkCacheShareAccountWithoutProtectionNeedsAttentionOnGeneralPage()
    {
        var configuration = new FoundryConfigurationDocument
        {
            NetworkCache = new NetworkCacheSettings
            {
                IsEnabled = true,
                Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }],
                Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a1" }]
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
    }

    [Theory]
    [InlineData(false, true, ConfigurationOverviewState.Disabled)]
    [InlineData(true, true, ConfigurationOverviewState.Configured)]
    [InlineData(true, false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_NetworkCacheState(bool isEnabled, bool isReady, ConfigurationOverviewState expected)
    {
        var configuration = new FoundryConfigurationDocument { NetworkCache = new NetworkCacheSettings { IsEnabled = isEnabled } };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsNetworkCacheConfigurationReady = isReady });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.NetworkCache]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(evaluation, ConfigurationNavigationTarget.NetworkCache));
    }

    [Theory]
    [InlineData(NetworkCachePublicationLevel.UpToDate, ConfigurationOverviewReason.None)]
    [InlineData(NetworkCachePublicationLevel.Outdated, ConfigurationOverviewReason.Outdated)]
    [InlineData(NetworkCachePublicationLevel.NotPublished, ConfigurationOverviewReason.NotPublished)]
    public void Evaluate_ActiveNetworkCacheIsConfiguredWhateverItsPublication(NetworkCachePublicationLevel publication,
        ConfigurationOverviewReason expectedReason)
    {
        var configuration = new FoundryConfigurationDocument { NetworkCache = new NetworkCacheSettings { IsEnabled = true } };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { NetworkCachePublication = publication });

        Assert.Equal(ConfigurationOverviewState.Configured, evaluation[ConfigurationOverviewItem.NetworkCache]);
        Assert.Equal(expectedReason, evaluation.GetReason(ConfigurationOverviewItem.NetworkCache));
        Assert.Equal(ConfigurationOverviewState.Configured,
            ConfigurationOverviewNavigationEvaluator.EvaluateTarget(evaluation, ConfigurationNavigationTarget.NetworkCache));
    }

    [Theory]
    [InlineData(false, true, ConfigurationOverviewState.Disabled)]
    [InlineData(true, false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_NotPublishedIsNotTheReasonOfADisabledOrInvalidNetworkCache(bool isEnabled, bool isReady, ConfigurationOverviewState expected)
    {
        var configuration = new FoundryConfigurationDocument { NetworkCache = new NetworkCacheSettings { IsEnabled = isEnabled } };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsNetworkCacheConfigurationReady = isReady, NetworkCachePublication = NetworkCachePublicationLevel.NotPublished });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.NetworkCache]);
        Assert.Equal(ConfigurationOverviewReason.None, evaluation.GetReason(ConfigurationOverviewItem.NetworkCache));
    }

    [Fact]
    public void Evaluate_AutomaticDomainWithoutProtectionNeedsAttentionOnGeneralPage()
    {
        var configuration = new FoundryConfigurationDocument
        {
            DomainJoin = new DomainJoinSettings { IsEnabled = true, Mode = DomainJoinMode.Automatic }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
    }

    [Theory]
    [InlineData(false, 0, true, ConfigurationOverviewState.Disabled)]
    [InlineData(true, 0, true, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, 1, true, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, 2, true, ConfigurationOverviewState.Configured)]
    [InlineData(true, 2, false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_PostInstallation_RequiresEnabledActionsAndContent(bool enabled, int actionState, bool contentReady, ConfigurationOverviewState expected)
    {
        var action = PreOobeActionSettings.Create(PreOobeActionKind.Command, "Command") with
        {
            IsEnabled = actionState == 2,
            Command = "echo ready"
        };
        var configuration = new FoundryConfigurationDocument
        {
            PreOobe = new() { IsEnabled = enabled, Actions = actionState == 0 ? [] : [action] }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsPostInstallationReady = contentReady });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.PostInstallation]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.PostInstallation));
    }

    [Theory]
    [InlineData(true, ConfigurationOverviewState.Downloading)]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_MissingPublishedContent_IsDownloadingOnlyWhileTheFetchRuns(bool isDownloading, ConfigurationOverviewState expected)
    {
        var action = PreOobeActionSettings.Create(PreOobeActionKind.Command, "Command") with { IsEnabled = true, Command = "echo ready" };
        var configuration = new FoundryConfigurationDocument
        {
            PreOobe = new() { IsEnabled = true, Actions = [action] },
            CustomDrivers = new CustomDriverSetsSettings { IsEnabled = true, Sets = [CreateDriverSet(isIncluded: true)] }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration) with
        {
            IsPostInstallationReady = false,
            IsCustomDriversReady = false,
            IsSharedContentDownloading = isDownloading
        });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.PostInstallation]);
        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.CustomDrivers]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(evaluation, ConfigurationNavigationTarget.PostInstallation));
    }

    [Fact]
    public void Evaluate_InvalidSettingsWhileDownloading_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomDrivers = new CustomDriverSetsSettings { IsEnabled = true, Sets = [CreateDriverSet(isIncluded: false)] }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomDriversReady = false, IsSharedContentDownloading = true });

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.CustomDrivers]);
    }

    [Theory]
    [InlineData(false, false, ConfigurationOverviewState.Disabled)]
    [InlineData(true, true, ConfigurationOverviewState.Configured)]
    [InlineData(true, false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_CustomImages_UsesSourceReadiness(bool enabled, bool ready, ConfigurationOverviewState expected)
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomImages = new CustomImagesSettings { IsEnabled = enabled }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomImagesReady = ready });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.CustomImages]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.CustomImages));
    }

    [Fact]
    public void CustomDriversItemIsNotReadyWhenEnabledWithoutIncludedSet()
    {
        var settings = new CustomDriverSetsSettings { IsEnabled = true, Sets = [CreateDriverSet(isIncluded: false)] };
        var configuration = new FoundryConfigurationDocument { CustomDrivers = settings };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomDriversReady = CustomDriverSetsSettingsValidator.IsReady(settings, _ => true) });

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.CustomDrivers]);
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.CustomDrivers));
    }

    [Theory]
    [InlineData(true, ConfigurationOverviewState.Configured)]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention)]
    public void CustomDriversItemIsNotReadyWhenLibraryContentIsMissing(bool isAvailable, ConfigurationOverviewState expected)
    {
        var settings = new CustomDriverSetsSettings { IsEnabled = true, Sets = [CreateDriverSet(isIncluded: true)] };
        var configuration = new FoundryConfigurationDocument { CustomDrivers = settings };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomDriversReady = CustomDriverSetsSettingsValidator.IsReady(settings, _ => isAvailable) });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.CustomDrivers]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.CustomDrivers));
    }

    [Fact]
    public void CustomDriversItemIsDisabledWhenSectionIsOff()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomDrivers = new CustomDriverSetsSettings { IsEnabled = false, Sets = [CreateDriverSet(isIncluded: true)] }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomDriversReady = false });

        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.CustomDrivers]);
        Assert.Equal(ConfigurationOverviewState.Disabled, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.CustomDrivers));
    }

    [Fact]
    public void Evaluate_CustomImagesForAnotherMediaArchitecture_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { Architecture = WinPeArchitecture.Arm64 },
            CustomImages = new CustomImagesSettings
            {
                IsEnabled = true,
                Images = [new CustomImageReference { Indexes = [new CustomImageIndex { Index = 1, Architecture = "x64" }] }]
            }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsCustomImagesReady = true });

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.CustomImages));
    }

    [Fact]
    public void Evaluate_DefaultAnswerFiles_AreDisabledInOverviewAndNavigation()
    {
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(new FoundryConfigurationDocument()));

        Assert.Equal(ConfigurationOverviewState.Disabled, evaluation[ConfigurationOverviewItem.Unattend]);
        Assert.Equal(ConfigurationOverviewState.Disabled, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.Unattend));
    }

    [Theory]
    [InlineData(true, ConfigurationOverviewState.Configured)]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention)]
    public void Evaluate_EnabledAnswerFiles_UsesSourceAndProtectionReadiness(bool isReady, ConfigurationOverviewState expected)
    {
        var configuration = new FoundryConfigurationDocument
        {
            Unattend = new UnattendSettings { IsEnabled = true }
        };
        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with { IsUnattendConfigurationReady = isReady });

        Assert.Equal(expected, evaluation[ConfigurationOverviewItem.Unattend]);
        Assert.Equal(expected, ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            evaluation, ConfigurationNavigationTarget.Unattend));
    }

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
    public void Evaluate_InvalidMachineNameComposition_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Mode = MachineNamingMode.Composed,
                    Components =
                    [
                        new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
                        new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "LAB" }
                    ]
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.MachineNaming]);
    }

    [Fact]
    public void Evaluate_ValidMachineNameComposition_IsConfigured()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Mode = MachineNamingMode.Composed,
                    Components =
                    [
                        new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
                        new MachineNameComponentSettings
                        {
                            Type = MachineNameComponentType.SerialNumber,
                            MaximumLength = 12,
                            Truncation = MachineNameTruncation.KeepRight
                        }
                    ],
                    Separator = MachineNameSeparator.Hyphen
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.Configured, evaluation[ConfigurationOverviewItem.MachineNaming]);
    }

    [Fact]
    public void Evaluate_InvalidOobeAdditionalAccount_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    AdditionalAccounts =
                    [
                        new OobeAdditionalAccountSettings
                        {
                            Id = "account-1",
                            UserName = "Tech/User",
                            Type = OobeAccountType.Standard
                        }
                    ]
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.Oobe]);
    }

    [Fact]
    public void Evaluate_OobeSecretMismatch_NeedsAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(configuration) with
            {
                IsOobeAccountConfigurationReady = false
            });

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.Oobe]);
    }

    [Fact]
    public void Evaluate_OobePasswordWithoutDeploymentProtection_MarksRelatedSettingsAsNeedingAttention()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    UseAdministratorPassword = true
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
        Assert.Equal(ConfigurationOverviewState.NeedsAttention, evaluation[ConfigurationOverviewItem.Oobe]);
    }

    [Theory]
    [InlineData(false, true, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, true, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, false, ConfigurationOverviewState.Configured)]
    public void Evaluate_OobeAccountsWhenAutopilotEnabled_OnlyAdditionalAccountsNeedAttention(
        bool enableAdministrator, bool includeAdditionalAccount, ConfigurationOverviewState expectedState)
    {
        var configuration = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.JsonProfile
            },
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = enableAdministrator,
                    AdditionalAccounts = includeAdditionalAccount ?
                    [
                        new OobeAdditionalAccountSettings
                        {
                            Id = "account-1",
                            UserName = "Technician",
                            Type = OobeAccountType.Standard
                        }
                    ] : []
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

        Assert.Equal(expectedState, evaluation[ConfigurationOverviewItem.Oobe]);
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

        Assert.Equal(1, evaluation.NeedsAttentionCount);
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

    [Theory]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, ConfigurationOverviewState.Configured, ConfigurationOverviewState.Configured)]
    public void Evaluate_ProvisionedWifiPassphrase_RequiresProtection(bool protectionEnabled, ConfigurationOverviewState wifi, ConfigurationOverviewState protection)
    {
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = protectionEnabled } },
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings { IsEnabled = true, Ssid = "Lab", SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration) with
        {
            NetworkInputs = new MediaInputs { Network = new NetworkSecrets { WifiPassphrase = "lab-passphrase" } },
            IsDeploymentProtectionSecretReady = protectionEnabled
        });

        Assert.Equal(wifi, evaluation[ConfigurationOverviewItem.Wifi]);
        Assert.Equal(protection, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
    }

    [Theory]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, ConfigurationOverviewState.Configured, ConfigurationOverviewState.Configured)]
    public void Evaluate_WiredClientPfx_RequiresProtection(bool protectionEnabled, ConfigurationOverviewState ethernet, ConfigurationOverviewState protection)
    {
        using var directory = new TemporaryDirectory();
        string template = CreateFile(directory, "wired.xml", "<LANProfile />");
        string client = CreateFile(directory, "client.pfx", "pfx");
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = protectionEnabled } },
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = TestFileReferences.For(template), ClientCertificate = TestFileReferences.For(client) }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration) with
        {
            NetworkInputs = TestFileReferences.Resolving(template, client),
            IsDeploymentProtectionSecretReady = protectionEnabled
        });

        Assert.Equal(ethernet, evaluation[ConfigurationOverviewItem.EthernetDot1x]);
        Assert.Equal(protection, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
    }

    [Theory]
    [InlineData(false, ConfigurationOverviewState.NeedsAttention, ConfigurationOverviewState.NeedsAttention)]
    [InlineData(true, ConfigurationOverviewState.Configured, ConfigurationOverviewState.Configured)]
    public void Evaluate_EnterpriseWifiClientCertificate_RequiresProtection(bool protectionEnabled, ConfigurationOverviewState wifi, ConfigurationOverviewState protection)
    {
        using var directory = new TemporaryDirectory();
        string template = CreateFile(directory, "wifi.xml", "<WLANProfile />");
        string client = CreateFile(directory, "wifi.pfx", "pfx");
        var configuration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = protectionEnabled } },
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "Corp",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                    HasEnterpriseProfile = true,
                    EnterpriseProfileTemplate = TestFileReferences.For(template),
                    ClientCertificate = TestFileReferences.For(client)
                }
            }
        };

        ConfigurationOverviewEvaluation evaluation = ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration) with
        {
            NetworkInputs = TestFileReferences.Resolving(template, client),
            IsDeploymentProtectionSecretReady = protectionEnabled
        });

        Assert.Equal(wifi, evaluation[ConfigurationOverviewItem.Wifi]);
        Assert.Equal(protection, evaluation[ConfigurationOverviewItem.DeploymentProtection]);
    }

    private static string CreateFile(TemporaryDirectory directory, string fileName, string content)
    {
        string path = Path.Combine(directory.Path, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static CustomDriverSetReference CreateDriverSet(bool isIncluded) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        DisplayName = "Synthetic drivers",
        Package = new ContentPackageReference { ContentHash = new string('a', 64), DisplayName = "Synthetic drivers", FileCount = 1, Length = 1 },
        IsIncluded = isIncluded
    };

    private static ConfigurationOverviewContext CreateContext(FoundryConfigurationDocument configuration)
    {
        return new ConfigurationOverviewContext
        {
            Configuration = configuration,
            NetworkInputs = MediaInputs.None,
            IsWinPeLanguageReady = true,
            IsCustomDriverConfigurationReady = true,
            IsDeploymentProtectionSecretReady = true,
            IsOobeAccountConfigurationReady = true,
            IsAutopilotConfigurationReady = true
        };
    }
}
