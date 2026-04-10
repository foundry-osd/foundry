using CommunityToolkit.Mvvm.Input;
using Foundry.Services.ApplicationShell;
using Foundry.Services.ApplicationUpdate;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class UpdateAvailableDialogViewModel : LocalizedViewModelBase
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly ApplicationUpdateInfo _updateInfo;

    public UpdateAvailableDialogViewModel(
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        ApplicationUpdateInfo updateInfo)
        : base(localizationService)
    {
        _applicationShellService = applicationShellService;
        _updateInfo = updateInfo ?? throw new ArgumentNullException(nameof(updateInfo));
    }

    public string CurrentVersion => _updateInfo.CurrentVersion;

    public string LatestVersion => _updateInfo.LatestVersion;

    public string ReleaseTitle => _updateInfo.ReleaseTitle;

    public string PublishedAtDisplay => _updateInfo.PublishedAt?.ToLocalTime().ToString("f", LocalizationService.CurrentCulture)
        ?? Strings["UpdateAvailablePublishedUnknown"];

    public string ReleaseNotes => string.IsNullOrWhiteSpace(_updateInfo.ReleaseNotes)
        ? Strings["UpdateAvailableNotesEmpty"]
        : _updateInfo.ReleaseNotes;

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void OpenRelease()
    {
        _applicationShellService.OpenUrl(_updateInfo.ReleaseUrl);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
