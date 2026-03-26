using CommunityToolkit.Mvvm.Input;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class AboutDialogViewModel : LocalizedViewModelBase
{
    private readonly IApplicationShellService _applicationShellService;

    public AboutDialogViewModel(
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService)
        : base(localizationService)
    {
        _applicationShellService = applicationShellService;
    }

    public string AppName => FoundryApplicationInfo.AppName;

    public string Version => FoundryApplicationInfo.Version;

    public string CheckForUpdatesLinkText => "github.com/mchave3/Foundry/releases/latest";

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void CheckForUpdates()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.LatestReleaseUrl);
    }

    [RelayCommand]
    private void OpenLicense()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.LicenseUrl);
    }

    [RelayCommand]
    private void OpenAuthors()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.AuthorsUrl);
    }

    [RelayCommand]
    private void OpenSupport()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.SupportUrl);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
