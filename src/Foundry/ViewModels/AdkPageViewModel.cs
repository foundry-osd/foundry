using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using Serilog;

namespace Foundry.ViewModels;

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
    public partial bool IsActionEnabled { get; set; }

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
        StatusTitle = string.Empty;
        StatusDescription = string.Empty;
        StatusSeverity = InfoBarSeverity.Informational;
        SetupActionTitle = string.Empty;
        SetupActionDescription = string.Empty;
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

        adkService.StatusChanged += OnAdkStatusChanged;
        operationProgressService.StateChanged += OnOperationProgressChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        ApplyStatus(adkService.CurrentStatus);
        ApplyOperationState(operationProgressService.State);
    }

    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        operationProgressService.StateChanged -= OnOperationProgressChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        AdkInstallationStatus status = await adkService.RefreshStatusAsync();
        ApplyShellState(status);
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

    private async Task RunBlockingAdkOperationAsync(Func<CancellationToken, Task<AdkInstallationStatus>> operation)
    {
        shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);

        try
        {
            AdkInstallationStatus status = await operation(CancellationToken.None);
            ApplyShellState(status);
        }
        catch (Exception ex)
        {
            logger.Warning("ADK page operation failed. ErrorMessage={ErrorMessage}", ex.Message);
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
        ReadinessDetailsTitle = localizationService.GetString("Adk.ReadinessDetails.Title");
        InstalledVersionTitle = localizationService.GetString("Adk.Version.InstalledTitle");
        InstalledVersion = status.InstalledVersion ?? localizationService.GetString("Adk.Version.NotDetected");
        RequiredVersionPolicyTitle = localizationService.GetString("Adk.Version.RequiredPolicyTitle");
        RequiredVersionPolicy = localizationService.GetString("Adk.Version.RequiredPolicy");
        WinPeAddonTitle = localizationService.GetString("Adk.WinPeAddon.Title");
        WinPeAddonStatus = status.IsWinPeAddonInstalled
            ? localizationService.GetString("Adk.WinPeAddon.Installed")
            : localizationService.GetString("Adk.WinPeAddon.Missing");
        MediaCapabilityTitle = localizationService.GetString("Adk.MediaCapability.Title");
        MediaCapabilityStatus = status.CanCreateMedia
            ? localizationService.GetString("Adk.MediaCapability.Ready")
            : localizationService.GetString("Adk.MediaCapability.Blocked");
        IsUpgradeButtonVisible = status.IsInstalled && !status.IsCompatible;
        IsInstallButtonVisible = !IsUpgradeButtonVisible && (!status.IsInstalled || !status.IsWinPeAddonInstalled);
        IsActionEnabled = !IsBusy;
    }

    private void ApplyOperationState(OperationProgressState state)
    {
        IsBusy = state.IsRunning;
        IsActionEnabled = !IsBusy;
    }

    private string GetStatusTitle(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return localizationService.GetString("Adk.Status.ReadyTitle");
        }

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

    private static InfoBarSeverity GetStatusSeverity(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return InfoBarSeverity.Success;
        }

        return status.IsInstalled && status.IsCompatible
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Error;
    }

    private string GetStatusDescription(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return localizationService.GetString("Adk.Status.ReadyDescription");
        }

        if (!status.IsInstalled)
        {
            return localizationService.GetString("Adk.Status.MissingDescription");
        }

        if (!status.IsCompatible)
        {
            return localizationService.GetString("Adk.Status.IncompatibleDescription");
        }

        return localizationService.GetString("Adk.Status.WinPeMissingDescription");
    }
}
