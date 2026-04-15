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

    public bool HasDistinctReleaseTitle =>
        !string.IsNullOrWhiteSpace(ReleaseTitle) &&
        !ReleaseTitle.Contains(LatestVersion, StringComparison.OrdinalIgnoreCase);

    public string PublishedAtDisplay => _updateInfo.PublishedAt?.ToLocalTime().ToString("f", LocalizationService.CurrentCulture)
        ?? Strings["UpdateAvailable.PublishedUnknown"];

    public string ReleaseNotesMarkdown => string.IsNullOrWhiteSpace(_updateInfo.ReleaseNotes)
        ? Strings["UpdateAvailable.NotesEmpty"]
        : _updateInfo.ReleaseNotes;

    public string AvailabilitySummary => string.Format(
        LocalizationService.CurrentCulture,
        Strings["UpdateAvailable.SummaryFormat"],
        _updateInfo.SummaryReleaseTitle,
        CurrentVersion);

    public event EventHandler? CloseRequested;

    public void OpenExternalUrl(string url)
    {
        _applicationShellService.OpenUrl(url);
    }

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
