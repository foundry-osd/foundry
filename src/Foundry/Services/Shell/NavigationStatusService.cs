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
            return Autopilot(configuration.Autopilot, AutopilotProvisioningMode.JsonProfile);
        }

        if (pageType == typeof(AutopilotZeroTouchPage))
        {
            return Autopilot(configuration.Autopilot, AutopilotProvisioningMode.HardwareHashUpload);
        }

        if (pageType == typeof(AutopilotInteractiveHashUploadPage))
        {
            return Autopilot(configuration.Autopilot, AutopilotProvisioningMode.InteractiveHardwareHashUpload);
        }

        if (pageType == typeof(OsSelectionPage))
        {
            return Standard(configuration.OperatingSystemSelection.IsEnabled);
        }

        CustomizationSettings customization = configuration.Customization;
        if (pageType == typeof(MachineNamingPage))
        {
            MachineNamingSettings settings = customization.MachineNaming;
            bool isValid = string.IsNullOrWhiteSpace(settings.Prefix) || ComputerNameRules.IsValid(settings.Prefix);
            return Standard(settings.IsEnabled && isValid);
        }

        if (pageType == typeof(OobePage))
        {
            return Standard(customization.Oobe.IsEnabled);
        }

        if (pageType == typeof(OptionalFeaturesPage))
        {
            WindowsOptionalFeatureSettings settings = customization.WindowsOptionalFeatures;
            return Standard(settings.IsEnabled &&
                (settings.EnabledFeatureIds.Count > 0 || settings.DisabledFeatureIds.Count > 0));
        }

        if (pageType == typeof(AppRemovalPage))
        {
            return Standard(customization.AppxRemoval.IsEnabled && customization.AppxRemoval.PackageNames.Count > 0);
        }

        if (pageType == typeof(AiComponentsPage))
        {
            AiComponentRemovalSettings settings = customization.AiComponentRemoval;
            return Standard(settings.IsEnabled && HasAiComponentAction(settings));
        }

        return null;
    }

    private static NavigationStatus Standard(bool isConfigured) => isConfigured
        ? Configured("NavigationStatus.Configured", NavigationInfoBadgeSeverity.Success)
        : new NavigationStatus(null, "NavigationStatus.NotConfigured");

    private static NavigationStatus Autopilot(AutopilotSettings settings, AutopilotProvisioningMode mode) =>
        settings.IsEnabled && settings.ProvisioningMode == mode
            ? Configured("NavigationStatus.ActiveProvisioningMode", NavigationInfoBadgeSeverity.Success)
            : new NavigationStatus(null, "NavigationStatus.NotConfigured");

    private static NavigationStatus Configured(string resourceKey, NavigationInfoBadgeSeverity severity) =>
        new(severity, resourceKey);

    private static bool HasAiComponentAction(AiComponentRemovalSettings settings) =>
        settings.RemoveCopilot ||
        settings.RemoveAiHub ||
        settings.DisableRecall ||
        settings.DisableClickToDo ||
        settings.DisableAiServiceAutoStart ||
        settings.DisableEdgeAi ||
        settings.DisablePaintAi ||
        settings.DisableNotepadAi;

    private void OnUnderlyingStatusChanged(object? sender, EventArgs e) =>
        StatusChanged?.Invoke(this, EventArgs.Empty);
}
