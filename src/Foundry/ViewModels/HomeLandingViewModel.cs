using System.Collections.ObjectModel;
using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class HomeLandingViewModel : ObservableObject, IDisposable
{
    private static readonly Uri DocumentationUri = new("https://foundry-osd.github.io/docs/intro");

    private readonly IAdkService adkService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IExternalProcessLauncher externalProcessLauncher;
    private readonly IDialogService dialogService;
    private readonly IAppDispatcher dispatcher;
    private readonly IJsonNavigationService navigationService;
    private readonly ILogger logger;

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string WelcomeText { get; set; }

    [ObservableProperty]
    public partial string IntroText { get; set; }

    [ObservableProperty]
    public partial string AdkCardTitle { get; set; }

    [ObservableProperty]
    public partial string AdkCardDescription { get; set; }

    [ObservableProperty]
    public partial string AdkInstalledVersionLabel { get; set; }

    [ObservableProperty]
    public partial string AdkInstalledVersion { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonLabel { get; set; }

    [ObservableProperty]
    public partial string WinPeAddonStatus { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity AdkSeverity { get; set; }

    public ObservableCollection<HomeActionItem> Actions { get; } = [];

    public HomeLandingViewModel(
        IAdkService adkService,
        IApplicationLocalizationService localizationService,
        IExternalProcessLauncher externalProcessLauncher,
        IDialogService dialogService,
        IAppDispatcher dispatcher,
        IJsonNavigationService navigationService,
        ILogger logger)
    {
        this.adkService = adkService;
        this.localizationService = localizationService;
        this.externalProcessLauncher = externalProcessLauncher;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.navigationService = navigationService;
        this.logger = logger.ForContext<HomeLandingViewModel>();

        PageTitle = string.Empty;
        WelcomeText = string.Empty;
        IntroText = string.Empty;
        AdkCardTitle = string.Empty;
        AdkCardDescription = string.Empty;
        AdkInstalledVersionLabel = string.Empty;
        AdkInstalledVersion = string.Empty;
        WinPeAddonLabel = string.Empty;
        WinPeAddonStatus = string.Empty;

        ApplyLocalizedText();
        ApplyAdkStatus(adkService.CurrentStatus);

        adkService.StatusChanged += OnAdkStatusChanged;
        localizationService.LanguageChanged += OnLanguageChanged;
    }

    [RelayCommand]
    public async Task RefreshAdkStatusAsync()
    {
        AdkInstallationStatus status = await adkService.RefreshStatusAsync();
        ApplyAdkStatus(status);
    }

    public async Task ExecuteActionAsync(HomeActionItem action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case HomeActionKind.OpenAdk:
                NavigateTo(typeof(AdkPage), "Adk.PageTitle");
                break;
            case HomeActionKind.ConfigureMedia:
                NavigateTo(typeof(GeneralConfigurationPage), "GeneralConfigurationPage_Title.Text");
                break;
            case HomeActionKind.ReviewAndStart:
                NavigateTo(typeof(StartPage), "StartPage_Title.Text");
                break;
            case HomeActionKind.OpenDocumentation:
                await OpenDocumentationAsync();
                break;
            default:
                logger.Warning("Unknown Home action. Kind={Kind}", action.Kind);
                break;
        }
    }

    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void NavigateTo(Type pageType, string titleResourceKey)
    {
        navigationService.NavigateTo(pageType, localizationService.GetString(titleResourceKey));
    }

    private async Task OpenDocumentationAsync()
    {
        try
        {
            await externalProcessLauncher.OpenUriAsync(DocumentationUri);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to open Foundry documentation from Home.");
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Home.DocumentationFallback.Title"),
                string.Format(
                    localizationService.GetString("Home.DocumentationFallback.Message"),
                    DocumentationUri.AbsoluteUri),
                localizationService.GetString("Common.Close")));
        }
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!dispatcher.TryEnqueue(() => ApplyAdkStatus(e.Status)))
        {
            logger.Warning("Failed to enqueue Home ADK status refresh.");
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!dispatcher.TryEnqueue(() =>
            {
                ApplyLocalizedText();
                ApplyAdkStatus(adkService.CurrentStatus);
            }))
        {
            logger.Warning(
                "Failed to enqueue Home localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyLocalizedText()
    {
        PageTitle = localizationService.GetString("Home.PageTitle");
        WelcomeText = localizationService.GetString("Home.Welcome");
        IntroText = localizationService.GetString("Home.Intro");
        AdkInstalledVersionLabel = localizationService.GetString("Home.AdkInstalledVersion");
        WinPeAddonLabel = localizationService.GetString("Home.WinPeAddon");

        Actions.Clear();
        Actions.Add(new HomeActionItem(
            HomeActionKind.OpenAdk,
            localizationService.GetString("Home.Action.OpenAdk.Title"),
            localizationService.GetString("Home.Action.OpenAdk.Description"),
            "\uE946"));
        Actions.Add(new HomeActionItem(
            HomeActionKind.ConfigureMedia,
            localizationService.GetString("Home.Action.ConfigureMedia.Title"),
            localizationService.GetString("Home.Action.ConfigureMedia.Description"),
            "\uE713"));
        Actions.Add(new HomeActionItem(
            HomeActionKind.ReviewAndStart,
            localizationService.GetString("Home.Action.ReviewAndStart.Title"),
            localizationService.GetString("Home.Action.ReviewAndStart.Description"),
            "\uE768"));
        Actions.Add(new HomeActionItem(
            HomeActionKind.OpenDocumentation,
            localizationService.GetString("Home.Action.OpenDocumentation.Title"),
            localizationService.GetString("Home.Action.OpenDocumentation.Description"),
            "\uE8A5"));
    }

    private void ApplyAdkStatus(AdkInstallationStatus status)
    {
        AdkCardTitle = GetAdkStatusTitle(status);
        AdkCardDescription = GetAdkStatusDescription(status);
        AdkInstalledVersion = status.InstalledVersion ?? localizationService.GetString("Adk.Version.NotDetected");
        WinPeAddonStatus = status.IsWinPeAddonInstalled
            ? localizationService.GetString("Adk.WinPeAddon.Installed")
            : localizationService.GetString("Adk.WinPeAddon.Missing");
        AdkSeverity = GetAdkSeverity(status);
    }

    private string GetAdkStatusTitle(AdkInstallationStatus status)
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

    private string GetAdkStatusDescription(AdkInstallationStatus status)
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

    private static InfoBarSeverity GetAdkSeverity(AdkInstallationStatus status)
    {
        if (status.CanCreateMedia)
        {
            return InfoBarSeverity.Success;
        }

        return status.IsInstalled && !status.IsCompatible
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Warning;
    }
}

public sealed record HomeActionItem(
    HomeActionKind Kind,
    string Title,
    string Description,
    string IconGlyph);

public enum HomeActionKind
{
    OpenAdk,
    ConfigureMedia,
    ReviewAndStart,
    OpenDocumentation
}
