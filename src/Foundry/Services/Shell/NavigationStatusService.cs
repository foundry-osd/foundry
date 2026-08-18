// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Views;

namespace Foundry.Services.Shell;

internal sealed class NavigationStatusService : INavigationStatusService
{
    private readonly IAdkService adkService;
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly INetworkSecretStateService networkSecretStateService;

    public NavigationStatusService(
        IAdkService adkService,
        IFoundryConfigurationStateService configurationStateService,
        INetworkSecretStateService networkSecretStateService)
    {
        this.adkService = adkService;
        this.configurationStateService = configurationStateService;
        this.networkSecretStateService = networkSecretStateService;
        adkService.StatusChanged += OnUnderlyingStatusChanged;
        configurationStateService.StateChanged += OnUnderlyingStatusChanged;
    }

    public event EventHandler? StatusChanged;

    public NavigationStatus? GetStatus(Type pageType)
    {
        FoundryConfigurationDocument configuration = configurationStateService.Current;
        if (pageType == typeof(AdkPage))
        {
            return adkService.CurrentStatus.CanCreateMedia
                ? Configured("NavigationStatus.AdkReady", NavigationInfoBadgeSeverity.Success)
                : Configured("NavigationStatus.AdkNotReady", NavigationInfoBadgeSeverity.Critical);
        }

        if (pageType == typeof(EthernetDot1xPage))
        {
            NetworkSettings network = networkSecretStateService.ApplyRequiredSecrets(configuration.Network) with
            {
                WifiProvisioned = false,
                Wifi = new WifiSettings()
            };
            return Standard(configuration.Network.Dot1x.IsEnabled && NetworkConfigurationValidator.Validate(network).IsValid);
        }

        if (pageType == typeof(WifiPage))
        {
            NetworkSettings network = networkSecretStateService.ApplyRequiredSecrets(configuration.Network) with
            {
                Dot1x = new Dot1xSettings()
            };
            return Standard(configuration.Network.WifiProvisioned && NetworkConfigurationValidator.Validate(network).IsValid);
        }

        if (pageType == typeof(AutopilotJsonProfilePage))
        {
            return AutopilotStatus(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.AutopilotJsonProfile));
        }

        if (pageType == typeof(AutopilotZeroTouchPage))
        {
            return AutopilotStatus(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.AutopilotHardwareHashUpload));
        }

        if (pageType == typeof(AutopilotInteractiveHashUploadPage))
        {
            return AutopilotStatus(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload));
        }

        if (pageType == typeof(OsSelectionPage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.OperatingSystemSelection));
        }

        if (pageType == typeof(MachineNamingPage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.MachineNaming));
        }

        if (pageType == typeof(OobePage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.Oobe));
        }

        if (pageType == typeof(OptionalFeaturesPage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.WindowsOptionalFeatures));
        }

        if (pageType == typeof(AppRemovalPage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.AppxRemoval));
        }

        if (pageType == typeof(AiComponentsPage))
        {
            return Standard(NavigationConfigurationStatusEvaluator.IsConfigured(
                configuration,
                ConfigurationNavigationTarget.AiComponentRemoval));
        }

        return null;
    }

    private static NavigationStatus Standard(bool isConfigured) => isConfigured
        ? Configured("NavigationStatus.Configured", NavigationInfoBadgeSeverity.Success)
        : new NavigationStatus(null, "NavigationStatus.NotConfigured");

    private static NavigationStatus AutopilotStatus(bool isConfigured) => isConfigured
        ? Configured("NavigationStatus.ActiveProvisioningMode", NavigationInfoBadgeSeverity.Success)
        : new NavigationStatus(null, "NavigationStatus.NotConfigured");

    private static NavigationStatus Configured(string resourceKey, NavigationInfoBadgeSeverity severity) =>
        new(severity, resourceKey);

    private void OnUnderlyingStatusChanged(object? sender, EventArgs e) =>
        StatusChanged?.Invoke(this, EventArgs.Empty);
}
