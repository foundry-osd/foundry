using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class HomeLandingViewModel : ObservableObject, IDisposable
{
    private readonly IAdkService adkService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
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
    public partial string AdkStatusTitle { get; set; }

    [ObservableProperty]
    public partial string AdkStatusDescription { get; set; }

    [ObservableProperty]
    public partial string InstalledVersionLabel { get; set; }

    [ObservableProperty]
    public partial string InstalledVersion { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonLabel { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonStatus { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity AdkStatusSeverity { get; set; }

    public string DocumentationUrl => FoundryApplicationInfo.DocumentationUrl;
    public string AdkNavigationTitle => localizationService.GetString("Adk.PageTitle");
    public string GeneralNavigationTitle => localizationService.GetString("GeneralConfigurationPage_Title.Text");
    public string StartNavigationTitle => localizationService.GetString("StartPage_Title.Text");

    public HomeLandingViewModel(
        IAdkService adkService,
        IApplicationLocalizationService localizationService,
        IAppDispatcher appDispatcher,
        ILogger logger)
    {
        this.adkService = adkService;
        this.localizationService = localizationService;
        this.appDispatcher = appDispatcher;
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
        AdkStatusTitle = string.Empty;
        AdkStatusDescription = string.Empty;
        InstalledVersionLabel = string.Empty;
        InstalledVersion = string.Empty;
        WinPeAddonLabel = string.Empty;
        WinPeAddonStatus = string.Empty;
        AdkStatusSeverity = InfoBarSeverity.Informational;

        adkService.StatusChanged += OnAdkStatusChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        ApplyLocalizedText();
    }

    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() => ApplyStatus(e.Status)))
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

    private void ApplyLocalizedText()
    {
        HeaderTitle = localizationService.GetString("Home.Header.Title");
        HeaderSubtitle = localizationService.GetString("Home.Header.Subtitle");
        OpenAdkTitle = localizationService.GetString("Home.Action.OpenAdk.Title");
        OpenAdkDescription = localizationService.GetString("Home.Action.OpenAdk.Description");
        ConfigureMediaTitle = localizationService.GetString("Home.Action.ConfigureMedia.Title");
        ConfigureMediaDescription = localizationService.GetString("Home.Action.ConfigureMedia.Description");
        ReviewAndStartTitle = localizationService.GetString("Home.Action.ReviewAndStart.Title");
        ReviewAndStartDescription = localizationService.GetString("Home.Action.ReviewAndStart.Description");
        OpenDocumentationTitle = localizationService.GetString("Home.Action.OpenDocumentation.Title");
        OpenDocumentationDescription = localizationService.GetString("Home.Action.OpenDocumentation.Description");
        InstalledVersionLabel = localizationService.GetString("Home.AdkStatus.InstalledVersionLabel");
        WinPeAddonLabel = localizationService.GetString("Home.AdkStatus.WinPeAddonLabel");

        ApplyStatus(adkService.CurrentStatus);
    }

    private void ApplyStatus(AdkInstallationStatus status)
    {
        AdkStatusTitle = GetStatusTitle(status);
        AdkStatusDescription = GetStatusDescription(status);
        InstalledVersion = status.InstalledVersion ?? localizationService.GetString("Adk.Version.NotDetected");
        WinPeAddonStatus = status.IsWinPeAddonInstalled
            ? localizationService.GetString("Adk.WinPeAddon.Installed")
            : localizationService.GetString("Adk.WinPeAddon.Missing");
        AdkStatusSeverity = status.CanCreateMedia ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
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
