// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Shell;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Provides localized home page content, ADK readiness status, and configuration
/// summary data for the workflow stepper and status cards.
/// </summary>
public sealed partial class HomeLandingViewModel : ObservableObject, IDisposable
{
    private readonly IAdkService adkService;
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly ILogger logger;

    [ObservableProperty]
    public partial string HeaderTitle { get; set; }

    [ObservableProperty]
    public partial string HeaderSubtitle { get; set; }

    [ObservableProperty]
    public partial string OpenAdkTitle { get; set; }

    [ObservableProperty]
    public partial string OpenAdkDescription { get; set; }

    [ObservableProperty]
    public partial string ConfigureMediaTitle { get; set; }

    [ObservableProperty]
    public partial string ConfigureMediaDescription { get; set; }

    [ObservableProperty]
    public partial string ReviewAndStartTitle { get; set; }

    [ObservableProperty]
    public partial string ReviewAndStartDescription { get; set; }

    [ObservableProperty]
    public partial string OpenDocumentationTitle { get; set; }

    [ObservableProperty]
    public partial string OpenDocumentationDescription { get; set; }

    [ObservableProperty]
    public partial string ScrollBackText { get; set; }

    [ObservableProperty]
    public partial string ScrollForwardText { get; set; }

    [ObservableProperty]
    public partial string StepperSectionTitle { get; set; }

    [ObservableProperty]
    public partial string Step1Label { get; set; }

    [ObservableProperty]
    public partial string Step2Label { get; set; }

    [ObservableProperty]
    public partial string Step3Label { get; set; }

    [ObservableProperty]
    public partial string Step1StatusText { get; set; }

    [ObservableProperty]
    public partial string Step2StatusText { get; set; }

    [ObservableProperty]
    public partial string Step3StatusText { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity Step1State { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity Step2State { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity Step3State { get; set; }

    [ObservableProperty]
    public partial bool IsWorkflowNavigationEnabled { get; set; }

    [ObservableProperty]
    public partial string AdkCardTitle { get; set; }

    [ObservableProperty]
    public partial string AdkCardStatusText { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity AdkCardSeverity { get; set; }

    [ObservableProperty]
    public partial string AdkVersionLabel { get; set; }

    [ObservableProperty]
    public partial string AdkVersionValue { get; set; }

    [ObservableProperty]
    public partial string AdkWinPeLabel { get; set; }

    [ObservableProperty]
    public partial string AdkWinPeValue { get; set; }

    [ObservableProperty]
    public partial string ConfigCardTitle { get; set; }

    [ObservableProperty]
    public partial string ConfigCardStatusText { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ConfigCardSeverity { get; set; }

    [ObservableProperty]
    public partial string ConfigArchitectureLabel { get; set; }

    [ObservableProperty]
    public partial string ConfigArchitectureValue { get; set; }

    [ObservableProperty]
    public partial string ConfigWinPeLanguageLabel { get; set; }

    [ObservableProperty]
    public partial string ConfigWinPeLanguageValue { get; set; }

    [ObservableProperty]
    public partial string ConfigSecureBootLabel { get; set; }

    [ObservableProperty]
    public partial string ConfigSecureBootValue { get; set; }

    [ObservableProperty]
    public partial string ConfigDriversLabel { get; set; }

    [ObservableProperty]
    public partial string ConfigDriversValue { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeLandingViewModel"/> class.
    /// </summary>
    public HomeLandingViewModel(
        IAdkService adkService,
        IFoundryConfigurationStateService configurationStateService,
        IApplicationLocalizationService localizationService,
        IAppDispatcher appDispatcher,
        IShellNavigationGuardService shellNavigationGuardService,
        ILogger logger)
    {
        this.adkService = adkService;
        this.configurationStateService = configurationStateService;
        this.localizationService = localizationService;
        this.appDispatcher = appDispatcher;
        this.shellNavigationGuardService = shellNavigationGuardService;
        this.logger = logger.ForContext<HomeLandingViewModel>();

        HeaderTitle = string.Empty;
        HeaderSubtitle = string.Empty;
        OpenAdkTitle = string.Empty;
        OpenAdkDescription = string.Empty;
        ConfigureMediaTitle = string.Empty;
        ConfigureMediaDescription = string.Empty;
        ReviewAndStartTitle = string.Empty;
        ReviewAndStartDescription = string.Empty;
        OpenDocumentationTitle = string.Empty;
        OpenDocumentationDescription = string.Empty;
        ScrollBackText = string.Empty;
        ScrollForwardText = string.Empty;
        StepperSectionTitle = string.Empty;
        Step1Label = string.Empty;
        Step2Label = string.Empty;
        Step3Label = string.Empty;
        Step1StatusText = string.Empty;
        Step2StatusText = string.Empty;
        Step3StatusText = string.Empty;
        Step1State = InfoBarSeverity.Informational;
        Step2State = InfoBarSeverity.Informational;
        Step3State = InfoBarSeverity.Informational;
        IsWorkflowNavigationEnabled = shellNavigationGuardService.State == ShellNavigationState.Ready;
        AdkCardTitle = string.Empty;
        AdkCardStatusText = string.Empty;
        AdkCardSeverity = InfoBarSeverity.Informational;
        AdkVersionLabel = string.Empty;
        AdkVersionValue = string.Empty;
        AdkWinPeLabel = string.Empty;
        AdkWinPeValue = string.Empty;
        ConfigCardTitle = string.Empty;
        ConfigCardStatusText = string.Empty;
        ConfigCardSeverity = InfoBarSeverity.Informational;
        ConfigArchitectureLabel = string.Empty;
        ConfigArchitectureValue = string.Empty;
        ConfigWinPeLanguageLabel = string.Empty;
        ConfigWinPeLanguageValue = string.Empty;
        ConfigSecureBootLabel = string.Empty;
        ConfigSecureBootValue = string.Empty;
        ConfigDriversLabel = string.Empty;
        ConfigDriversValue = string.Empty;

        adkService.StatusChanged += OnAdkStatusChanged;
        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        shellNavigationGuardService.StateChanged += OnShellNavigationStateChanged;

        ApplyLocalizedText();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        shellNavigationGuardService.StateChanged -= OnShellNavigationStateChanged;
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() => ApplyAdkStatus(e.Status)))
        {
            logger.Warning(
                "Failed to enqueue Home ADK status refresh. IsInstalled={IsInstalled}, IsCompatible={IsCompatible}, IsWinPeAddonInstalled={IsWinPeAddonInstalled}",
                e.Status.IsInstalled,
                e.Status.IsCompatible,
                e.Status.IsWinPeAddonInstalled);
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(ApplyLocalizedText))
        {
            logger.Warning(
                "Failed to enqueue Home localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (!appDispatcher.TryEnqueue(ApplyConfigurationSummary))
        {
            logger.Warning("Failed to enqueue Home configuration summary refresh.");
        }
    }

    private void OnShellNavigationStateChanged(object? sender, EventArgs e)
    {
        if (!appDispatcher.TryEnqueue(ApplyNavigationAvailability))
        {
            logger.Warning(
                "Failed to enqueue Home navigation availability refresh. ShellNavigationState={ShellNavigationState}",
                shellNavigationGuardService.State);
        }
    }

    private void ApplyNavigationAvailability()
    {
        IsWorkflowNavigationEnabled = shellNavigationGuardService.State == ShellNavigationState.Ready;
    }

    private void ApplyLocalizedText()
    {
        HeaderTitle = FoundryApplicationInfo.AppNameAndVersion;
        HeaderSubtitle = localizationService.GetString("Home.Header.Subtitle");
        OpenAdkTitle = localizationService.GetString("Home.Action.OpenAdk.Title");
        OpenAdkDescription = localizationService.GetString("Home.Action.OpenAdk.Description");
        ConfigureMediaTitle = localizationService.GetString("Home.Action.ConfigureMedia.Title");
        ConfigureMediaDescription = localizationService.GetString("Home.Action.ConfigureMedia.Description");
        ReviewAndStartTitle = localizationService.GetString("Home.Action.ReviewAndStart.Title");
        ReviewAndStartDescription = localizationService.GetString("Home.Action.ReviewAndStart.Description");
        OpenDocumentationTitle = localizationService.GetString("Home.Action.OpenDocumentation.Title");
        OpenDocumentationDescription = localizationService.GetString("Home.Action.OpenDocumentation.Description");
        ScrollBackText = localizationService.GetString("Home.Scroll.Back");
        ScrollForwardText = localizationService.GetString("Home.Scroll.Forward");

        StepperSectionTitle = localizationService.GetString("Home.Status.SectionTitle");
        Step1Label = localizationService.GetString("Nav_AdkKey.Title");
        Step2Label = localizationService.GetString("Nav_GeneralConfigurationKey.Title");
        Step3Label = localizationService.GetString("Nav_StartKey.Title");
        AdkCardTitle = localizationService.GetString("Nav_AdkKey.Title");
        AdkVersionLabel = localizationService.GetString("Home.AdkStatus.InstalledVersionLabel");
        AdkWinPeLabel = localizationService.GetString("Home.AdkStatus.WinPeAddonLabel");
        ConfigCardTitle = localizationService.GetString("Home.Status.ConfigCard.Title");
        ConfigArchitectureLabel = localizationService.GetString("StartMedia.Field.Architecture");
        ConfigWinPeLanguageLabel = localizationService.GetString("StartMedia.Field.WinPeLanguage");
        ConfigSecureBootLabel = localizationService.GetString("Home.Status.ConfigCard.SecureBootLabel");
        ConfigDriversLabel = localizationService.GetString("StartMedia.Field.Drivers");

        ApplyAdkStatus(adkService.CurrentStatus);
        ApplyConfigurationSummary();
    }

    private void ApplyAdkStatus(AdkInstallationStatus status)
    {
        bool ready = status.CanCreateMedia;

        AdkCardSeverity = ready ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        AdkCardStatusText = ready
            ? localizationService.GetString("Adk.Status.ReadyTitle")
            : GetAdkBlockingTitle(status);

        AdkVersionValue = status.InstalledVersion ?? localizationService.GetString("Adk.Version.NotDetected");
        AdkWinPeValue = status.IsWinPeAddonInstalled
            ? localizationService.GetString("Adk.WinPeAddon.Installed")
            : localizationService.GetString("Adk.WinPeAddon.Missing");

        ApplyWorkflowReadiness(ready, IsConfigurationReady());
    }

    private void ApplyConfigurationSummary()
    {
        GeneralSettings general = configurationStateService.Current.General;
        bool adkReady = adkService.CurrentStatus.CanCreateMedia;

        ConfigArchitectureValue = general.Architecture switch
        {
            WinPeArchitecture.Arm64 => "arm64",
            _ => "x64",
        };

        ConfigWinPeLanguageValue = string.IsNullOrWhiteSpace(general.WinPeLanguage)
            ? localizationService.GetString("Common.AutomaticOption")
            : general.WinPeLanguage;

        ConfigSecureBootValue = general.UseCa2023
            ? localizationService.GetString("Common.Enabled")
            : localizationService.GetString("Common.Disabled");

        List<string> drivers = [];
        if (general.IncludeDellDrivers)
        {
            drivers.Add(localizationService.GetString("StartMedia.DriverVendor.Dell"));
        }

        if (general.IncludeHpDrivers)
        {
            drivers.Add(localizationService.GetString("StartMedia.DriverVendor.Hp"));
        }

        if (!string.IsNullOrWhiteSpace(general.CustomDriverDirectoryPath))
        {
            drivers.Add(localizationService.GetString("Home.Status.ConfigCard.CustomDrivers"));
        }

        ConfigDriversValue = drivers.Count > 0
            ? string.Join(", ", drivers)
            : localizationService.GetString("Home.Status.ConfigCard.NoDrivers");

        ApplyWorkflowReadiness(adkReady, IsConfigurationReady());
    }

    private bool IsConfigurationReady()
    {
        return configurationStateService.IsNetworkConfigurationReady &&
               configurationStateService.IsDeployConfigurationReady &&
               configurationStateService.IsConnectProvisioningReady &&
               configurationStateService.AreRequiredSecretsReady &&
               (!configurationStateService.IsAutopilotEnabled || configurationStateService.IsAutopilotConfigurationReady);
    }

    private void ApplyWorkflowReadiness(bool adkReady, bool configurationReady)
    {
        bool workflowReady = adkReady && configurationReady;
        string readyText = localizationService.GetString("StartMedia.Readiness.State.Ready");
        string blockedText = localizationService.GetString("StartMedia.Readiness.State.Blocked");

        Step1State = adkReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        Step1StatusText = adkReady ? readyText : AdkCardStatusText;
        Step2State = !adkReady
            ? InfoBarSeverity.Informational
            : configurationReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        Step2StatusText = workflowReady ? readyText : blockedText;
        Step3State = workflowReady ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        Step3StatusText = workflowReady ? readyText : blockedText;

        ConfigCardSeverity = !adkReady
            ? InfoBarSeverity.Informational
            : configurationReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ConfigCardStatusText = !adkReady
            ? localizationService.GetString("Home.Status.ConfigCard.BlockedStatus")
            : configurationReady ? readyText : blockedText;
    }

    private string GetAdkBlockingTitle(AdkInstallationStatus status)
    {
        if (!status.IsInstalled)
        {
            return localizationService.GetString("Adk.Status.MissingTitle");
        }

        if (!status.IsCompatible)
        {
            return localizationService.GetString("Adk.Status.IncompatibleTitle");
        }

        return localizationService.GetString("Adk.Status.WinPeMissingTitle");
    }
}
