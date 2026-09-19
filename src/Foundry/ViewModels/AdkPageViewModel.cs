// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the ADK readiness page and install or upgrade actions.
/// </summary>
public sealed partial class AdkPageViewModel : ObservableObject, IDisposable
{
    private readonly IAdkService adkService;
    private readonly IOperationProgressService operationProgressService;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
    private readonly ILogger logger;

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string PageDescription { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; }

    [ObservableProperty]
    public partial string StatusDescription { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    [ObservableProperty]
    public partial string SetupActionTitle { get; set; }

    [ObservableProperty]
    public partial string SetupActionDescription { get; set; }

    [ObservableProperty]
    public partial string UpgradeButtonText { get; set; }

    [ObservableProperty]
    public partial string ReadinessDetailsTitle { get; set; }

    [ObservableProperty]
    public partial string InstalledVersionTitle { get; set; }

    [ObservableProperty]
    public partial string InstalledVersion { get; set; }

    [ObservableProperty]
    public partial string RequiredVersionPolicyTitle { get; set; }

    [ObservableProperty]
    public partial string RequiredVersionPolicy { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonTitle { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonStatus { get; set; }

    [ObservableProperty]
    public partial string MediaCapabilityTitle { get; set; }

    [ObservableProperty]
    public partial string MediaCapabilityStatus { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsInstallButtonVisible { get; set; }

    [ObservableProperty]
    public partial bool IsUpgradeButtonVisible { get; set; }

    [ObservableProperty]
    public partial bool IsSetupActionVisible { get; set; }

    [ObservableProperty]
    public partial bool IsActionEnabled { get; set; }

    [ObservableProperty]
    public partial string ServicingInstructionsText { get; set; }

    [ObservableProperty]
    public partial string RefreshButtonText { get; set; }

    public string DocumentationUrl => FoundryApplicationInfo.AdkDocumentationUrl;
    public Uri ServicingInstructionsUri => new("https://learn.microsoft.com/windows-hardware/get-started/adk-servicing");

    /// <summary>
    /// Initializes a new instance of the <see cref="AdkPageViewModel"/> class.
    /// </summary>
    public AdkPageViewModel(
        IAdkService adkService,
        IOperationProgressService operationProgressService,
        IShellNavigationGuardService shellNavigationGuardService,
        IApplicationLocalizationService localizationService,
        IAppDispatcher appDispatcher,
        ILogger logger)
    {
        this.adkService = adkService;
        this.operationProgressService = operationProgressService;
        this.shellNavigationGuardService = shellNavigationGuardService;
        this.localizationService = localizationService;
        this.appDispatcher = appDispatcher;
        this.logger = logger.ForContext<AdkPageViewModel>();

        PageTitle = localizationService.GetString("Adk.PageTitle");
        PageDescription = localizationService.GetString("Adk.PageDescription");
        StatusTitle = string.Empty;
        StatusDescription = string.Empty;
        StatusSeverity = InfoBarSeverity.Informational;
        SetupActionTitle = string.Empty;
        SetupActionDescription = string.Empty;
        UpgradeButtonText = string.Empty;
        ReadinessDetailsTitle = string.Empty;
        InstalledVersionTitle = string.Empty;
        InstalledVersion = string.Empty;
        RequiredVersionPolicyTitle = string.Empty;
        RequiredVersionPolicy = string.Empty;
        WinPeAddonTitle = string.Empty;
        WinPeAddonStatus = string.Empty;
        MediaCapabilityTitle = string.Empty;
        MediaCapabilityStatus = string.Empty;
        IsActionEnabled = true;
        ServicingInstructionsText = string.Empty;
        RefreshButtonText = string.Empty;

        adkService.StatusChanged += OnAdkStatusChanged;
        operationProgressService.StateChanged += OnOperationProgressChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        ApplyStatus(adkService.CurrentStatus);
        ApplyOperationState(operationProgressService.State);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        operationProgressService.StateChanged -= OnOperationProgressChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    [RelayCommand]
    private Task InstallAdkAsync()
    {
        return RunBlockingAdkOperationAsync(adkService.InstallAsync);
    }

    [RelayCommand]
    private Task UpgradeAdkAsync()
    {
        return RunBlockingAdkOperationAsync(adkService.UpgradeAsync);
    }

    [RelayCommand]
    private Task RefreshStatusAsync() => RunBlockingAdkOperationAsync(adkService.RefreshStatusAsync);

    private async Task RunBlockingAdkOperationAsync(Func<CancellationToken, Task<AdkInstallationStatus>> operation)
    {
        if (shellNavigationGuardService.State is ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending) return;
        // Keep navigation blocked until setup or a status refresh establishes the new readiness state.
        shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);

        try
        {
            AdkInstallationStatus status = await operation(CancellationToken.None);
            ApplyShellState(status);
        }
        catch (Exception)
        {
            shellNavigationGuardService.SetState(ShellNavigationState.AdkBlocked);
        }
    }

    private void ApplyShellState(AdkInstallationStatus status)
    {
        shellNavigationGuardService.SetState(status.CanCreateMedia ? ShellNavigationState.Ready : ShellNavigationState.AdkBlocked);
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() => ApplyStatus(e.Status)))
        {
            logger.Warning(
                "Failed to enqueue ADK page status refresh. IsInstalled={IsInstalled}, IsCompatible={IsCompatible}, IsWinPeAddonInstalled={IsWinPeAddonInstalled}",
                e.Status.IsInstalled,
                e.Status.IsCompatible,
                e.Status.IsWinPeAddonInstalled);
        }
    }

    private void OnOperationProgressChanged(object? sender, OperationProgressChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() => ApplyOperationState(e.State)))
        {
            logger.Warning(
                "Failed to enqueue ADK page operation refresh. OperationKind={OperationKind}, Progress={Progress}",
                e.State.Kind,
                e.State.Progress);
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() =>
        {
            PageTitle = localizationService.GetString("Adk.PageTitle");
            PageDescription = localizationService.GetString("Adk.PageDescription");
            ApplyStatus(adkService.CurrentStatus);
            ApplyOperationState(operationProgressService.State);
        }))
        {
            logger.Warning(
                "Failed to enqueue ADK page localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyStatus(AdkInstallationStatus status)
    {
        StatusTitle = GetStatusTitle(status);
        StatusDescription = GetStatusDescription(status);
        StatusSeverity = GetStatusSeverity(status);
        SetupActionTitle = localizationService.GetString("Adk.SetupAction.Title");
        SetupActionDescription = localizationService.GetString("Adk.SetupAction.Description");
        UpgradeButtonText = GetUpgradeButtonText(status);
        ReadinessDetailsTitle = localizationService.GetString("Adk.ReadinessDetails.Title");
        InstalledVersionTitle = localizationService.GetString("Adk.Version.InstalledTitle");
        InstalledVersion = status.InstalledVersion ?? localizationService.GetString("Adk.Version.NotDetected");
        RequiredVersionPolicyTitle = localizationService.GetString("Adk.Version.RequiredPolicyTitle");
        RequiredVersionPolicy = localizationService.GetString("Adk.Version.RequiredPolicy");
        WinPeAddonTitle = localizationService.GetString("Adk.WinPeAddon.Title");
        WinPeAddonStatus = status.IsWinPeAddonInstalled
            ? localizationService.GetString("Adk.WinPeAddon.Installed")
            : localizationService.GetString("Adk.WinPeAddon.Missing");
        if (status.IsWinPeAddonCompatible)
        {
            WinPeAddonStatus += $" (x64: {FormatAvailability(status.IsX64Available)}, ARM64: {FormatAvailability(status.IsArm64Available)})";
        }
        MediaCapabilityTitle = localizationService.GetString("Adk.MediaCapability.Title");
        MediaCapabilityStatus = status.CanCreateMedia
            ? localizationService.GetString("Adk.MediaCapability.Ready")
            : localizationService.GetString("Adk.MediaCapability.Blocked");
        IsUpgradeButtonVisible = status.IsInstalled && !status.IsCompatible;
        IsInstallButtonVisible = !IsUpgradeButtonVisible && (!status.IsInstalled
            || (!status.IsWinPeAddonInstalled && !status.IsWinPeAddonRegistered));
        IsSetupActionVisible = IsInstallButtonVisible || IsUpgradeButtonVisible;
        IsActionEnabled = !IsBusy;
        ServicingInstructionsText = localizationService.GetString("Adk.Servicing.Instructions");
        RefreshButtonText = localizationService.GetString("Common.Refresh");
    }

    private string FormatAvailability(bool available) => localizationService.GetString(
        available ? "Adk.WinPeAddon.FilesAvailable" : "Adk.WinPeAddon.FilesMissing");

    private void ApplyOperationState(OperationProgressState state)
    {
        IsBusy = state.IsRunning;
        IsActionEnabled = !IsBusy;
    }

    private string GetStatusTitle(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return localizationService.GetString(status.ServicingState == AdkServicingState.Verified
                ? "Adk.Status.ReadyTitle" : "Adk.Status.ServicingTitle");
        }

        if (!status.IsInstalled)
        {
            return localizationService.GetString("Adk.Status.MissingTitle");
        }

        if (!status.IsCompatible)
        {
            return localizationService.GetString("Adk.Status.IncompatibleTitle");
        }

        if (!status.IsWinPeAddonInstalled && !status.IsWinPeAddonRegistered) return localizationService.GetString("Adk.Status.WinPeMissingTitle");
        return localizationService.GetString("Adk.Status.WinPeInvalidTitle");
    }

    private static InfoBarSeverity GetStatusSeverity(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return status.ServicingState == AdkServicingState.Verified && status.IsX64Available && status.IsArm64Available
                ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        }

        return status.IsInstalled && status.IsCompatible
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Error;
    }

    private string GetStatusDescription(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            string description = localizationService.GetString(status.IsX64Available && status.IsArm64Available
                ? "Adk.Status.ReadyDescription" : "Adk.Status.WinPeInvalidDescription");
            if (status.ServicingState == AdkServicingState.Verified) return description;

            string servicingDescription = status.ServicingState == AdkServicingState.Unknown
                ? localizationService.GetString("Adk.Status.ServicingUnknownDescription")
                : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    localizationService.GetString("Adk.Status.ServicingRecommendedDescription"), AdkInstallationDetector.RecommendedServicingUpdate);
            return $"{description}{Environment.NewLine}{servicingDescription}";
        }

        if (!status.IsInstalled)
        {
            return localizationService.GetString("Adk.Status.MissingDescription");
        }

        if (!status.IsCompatible)
        {
            return localizationService.GetString("Adk.Status.IncompatibleDescription");
        }

        if (!status.IsWinPeAddonInstalled && !status.IsWinPeAddonRegistered) return localizationService.GetString("Adk.Status.WinPeMissingDescription");
        return localizationService.GetString("Adk.Status.WinPeInvalidDescription");
    }

    private string GetUpgradeButtonText(AdkInstallationStatus status)
    {
        return status.VersionRelation == AdkVersionRelation.AboveSupported
            ? localizationService.GetString("AdkPage_DowngradeButton.Content")
            : localizationService.GetString("AdkPage_UpgradeButton.Content");
    }
}
