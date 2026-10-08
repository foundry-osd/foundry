// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Views;

namespace Foundry.Services.Shell;

internal sealed class NavigationStatusService : INavigationStatusService
{
    private readonly IAdkService adkService;
    private readonly IConfigurationOverviewService configurationOverviewService;

    public NavigationStatusService(
        IAdkService adkService,
        IConfigurationOverviewService configurationOverviewService)
    {
        this.adkService = adkService;
        this.configurationOverviewService = configurationOverviewService;
        configurationOverviewService.Changed += OnUnderlyingStatusChanged;
    }

    public event EventHandler? StatusChanged;

    public NavigationStatus? GetStatus(Type pageType)
    {
        if (pageType == typeof(AdkPage))
        {
            return adkService.CurrentStatus.CanCreateMedia
                ? CreateStatus("NavigationStatus.AdkReady", NavigationInfoBadgeSeverity.Success)
                : CreateStatus("NavigationStatus.AdkNotReady", NavigationInfoBadgeSeverity.Critical);
        }

        ConfigurationNavigationTarget target = ResolveTarget(pageType);
        if (target == ConfigurationNavigationTarget.None)
        {
            return null;
        }

        ConfigurationOverviewEvaluation overview = configurationOverviewService.Evaluate();
        ConfigurationOverviewState state = ConfigurationOverviewNavigationEvaluator.EvaluateTarget(overview, target);
        if (target == ConfigurationNavigationTarget.General && state != ConfigurationOverviewState.NeedsAttention)
        {
            return null;
        }

        bool isProvisioningModeTarget = target is ConfigurationNavigationTarget.AutopilotJsonProfile or
            ConfigurationNavigationTarget.AutopilotHardwareHashUpload or
            ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload or
            ConfigurationNavigationTarget.DomainJoinInteractive or ConfigurationNavigationTarget.DomainJoinAutomatic;
        return ToNavigationStatus(state, isProvisioningModeTarget);
    }

    private static ConfigurationNavigationTarget ResolveTarget(Type pageType)
    {
        if (pageType == typeof(InteractiveDomainJoinPage)) return ConfigurationNavigationTarget.DomainJoinInteractive;
        if (pageType == typeof(ZeroTouchDomainJoinPage)) return ConfigurationNavigationTarget.DomainJoinAutomatic;
        if (pageType == typeof(GeneralConfigurationPage))
        {
            return ConfigurationNavigationTarget.General;
        }

        if (pageType == typeof(EthernetDot1xPage))
        {
            return ConfigurationNavigationTarget.EthernetDot1x;
        }

        if (pageType == typeof(WifiPage))
        {
            return ConfigurationNavigationTarget.Wifi;
        }

        if (pageType == typeof(AutopilotJsonProfilePage))
        {
            return ConfigurationNavigationTarget.AutopilotJsonProfile;
        }

        if (pageType == typeof(AutopilotZeroTouchPage))
        {
            return ConfigurationNavigationTarget.AutopilotHardwareHashUpload;
        }

        if (pageType == typeof(AutopilotInteractiveHashUploadPage))
        {
            return ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload;
        }

        if (pageType == typeof(OsSelectionPage))
        {
            return ConfigurationNavigationTarget.OperatingSystemSelection;
        }

        if (pageType == typeof(MachineNamingPage))
        {
            return ConfigurationNavigationTarget.MachineNaming;
        }

        if (pageType == typeof(CustomImagesPage))
        {
            return ConfigurationNavigationTarget.CustomImages;
        }

        if (pageType == typeof(PostInstallationPage))
        {
            return ConfigurationNavigationTarget.PostInstallation;
        }

        if (pageType == typeof(UnattendPage))
        {
            return ConfigurationNavigationTarget.Unattend;
        }

        if (pageType == typeof(OobePage))
        {
            return ConfigurationNavigationTarget.Oobe;
        }

        if (pageType == typeof(OptionalFeaturesPage))
        {
            return ConfigurationNavigationTarget.WindowsOptionalFeatures;
        }

        if (pageType == typeof(AppRemovalPage))
        {
            return ConfigurationNavigationTarget.AppxRemoval;
        }

        return pageType == typeof(AiComponentsPage)
            ? ConfigurationNavigationTarget.AiComponentRemoval
            : ConfigurationNavigationTarget.None;
    }

    private static NavigationStatus ToNavigationStatus(
        ConfigurationOverviewState state,
        bool isProvisioningModeTarget)
    {
        return state switch
        {
            ConfigurationOverviewState.NeedsAttention => CreateStatus(
                "Common.NeedsAttention",
                NavigationInfoBadgeSeverity.Critical),
            ConfigurationOverviewState.Configured or ConfigurationOverviewState.Default => CreateStatus(
                isProvisioningModeTarget ? "NavigationStatus.ActiveProvisioningMode" : "NavigationStatus.Configured",
                NavigationInfoBadgeSeverity.Success),
            _ => new NavigationStatus(null, "NavigationStatus.NotConfigured")
        };
    }

    private static NavigationStatus CreateStatus(string resourceKey, NavigationInfoBadgeSeverity severity) =>
        new(severity, resourceKey);

    private void OnUnderlyingStatusChanged(object? sender, EventArgs e) =>
        StatusChanged?.Invoke(this, EventArgs.Empty);
}
