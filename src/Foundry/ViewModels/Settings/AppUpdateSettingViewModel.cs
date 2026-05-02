using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Updates;

namespace Foundry.ViewModels
{
    public sealed partial class AppUpdateSettingViewModel : ObservableObject, IDisposable
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IDialogService dialogService;
        private readonly IApplicationUpdateService applicationUpdateService;
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
        private ApplicationUpdateCheckResult? currentCheckResult;
        private string releaseNotes;

        [ObservableProperty]
        public partial string CurrentVersion { get; set; }

        [ObservableProperty]
        public partial string LastUpdateCheck { get; set; }

        [ObservableProperty]
        public partial bool IsUpdateAvailable { get; set; }

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial bool IsCheckButtonEnabled { get; set; }

        [ObservableProperty]
        public partial string LoadingStatus { get; set; }

        [ObservableProperty]
        public partial int DownloadProgress { get; set; }

        [ObservableProperty]
        public partial bool IsInstallButtonVisible { get; set; }

        [ObservableProperty]
        public partial bool IsReleaseNotesVisible { get; set; }

        public string UpdateChannel => appSettingsService.Current.Updates.Channel;
        public string UpdateFeedUrl => appSettingsService.Current.Updates.FeedUrl;

        public AppUpdateSettingViewModel(
            IAppSettingsService appSettingsService,
            IDialogService dialogService,
            IApplicationUpdateService applicationUpdateService,
            IApplicationUpdateStateService updateStateService,
            IApplicationLocalizationService localizationService,
            IAppDispatcher appDispatcher)
        {
            this.appSettingsService = appSettingsService;
            this.dialogService = dialogService;
            this.applicationUpdateService = applicationUpdateService;
            this.updateStateService = updateStateService;
            this.localizationService = localizationService;
            this.appDispatcher = appDispatcher;
            releaseNotes = localizationService.GetString("Update.ReleaseNotesFallback");

            CurrentVersion = localizationService.FormatString("Update.CurrentVersionFormat", FoundryApplicationInfo.Version);
            LastUpdateCheck = appSettingsService.Current.Updates.LastCheckedAt?.ToString("yyyy-MM-dd HH:mm") ?? localizationService.GetString("Update.NotChecked");
            IsCheckButtonEnabled = true;
            LoadingStatus = localizationService.GetString("Update.Status.Ready");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyCurrentUpdateState(updateStateService.CurrentResult);
        }

        public void Dispose()
        {
            updateStateService.StateChanged -= OnUpdateStateChanged;
            localizationService.LanguageChanged -= OnLanguageChanged;
        }

        [RelayCommand]
        private async Task CheckForUpdateAsync()
        {
            IsLoading = true;
            IsUpdateAvailable = false;
            IsInstallButtonVisible = false;
            IsReleaseNotesVisible = false;
            IsCheckButtonEnabled = false;
            DownloadProgress = 0;
            LoadingStatus = localizationService.GetString("Update.Status.Checking");

            try
            {
                ApplicationUpdateCheckResult result = await applicationUpdateService.CheckForUpdatesAsync();
                DateTimeOffset checkedAt = DateTimeOffset.Now;
                appSettingsService.Current.Updates.LastCheckedAt = checkedAt;
                appSettingsService.Save();

                LastUpdateCheck = checkedAt.ToString("yyyy-MM-dd HH:mm");
                ApplyCurrentUpdateState(result);
            }
            finally
            {
                IsLoading = false;
                IsCheckButtonEnabled = true;
            }
        }

        [RelayCommand]
        private async Task DownloadAndRestartUpdateAsync()
        {
            IsLoading = true;
            IsCheckButtonEnabled = false;
            IsInstallButtonVisible = false;
            LoadingStatus = localizationService.GetString("Update.Status.Downloading");
            DownloadProgress = 0;

            try
            {
                bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
                    localizationService.GetString("Update.ConfirmDownloadRestart.Title"),
                    localizationService.GetString("Update.ConfirmDownloadRestart.Message"),
                    localizationService.GetString("Update.ConfirmDownloadRestart.PrimaryButton"),
                    localizationService.GetString("Common.Cancel")));

                if (!confirmed)
                {
                    LoadingStatus = currentCheckResult is not null
                        ? GetCheckStatusMessage(currentCheckResult)
                        : localizationService.GetString("Update.Status.Ready");
                    IsInstallButtonVisible = IsUpdateAvailable;
                    return;
                }

                Progress<int> progress = new(value =>
                {
                    DownloadProgress = value;
                    LoadingStatus = localizationService.FormatString("Update.Status.DownloadingProgressFormat", value);
                });

                ApplicationUpdateDownloadResult result = await applicationUpdateService.DownloadUpdateAsync(progress);
                LoadingStatus = result.Message;

                if (result.Status == ApplicationUpdateStatus.ReadyToRestart)
                {
                    applicationUpdateService.ApplyUpdateAndRestart();
                }
                else
                {
                    IsInstallButtonVisible = IsUpdateAvailable;
                }
            }
            finally
            {
                IsLoading = false;
                IsCheckButtonEnabled = true;
            }
        }

        [RelayCommand]
        private Task GetReleaseNotesAsync()
        {
            return dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Update.ReleaseNotesDialogTitle"),
                releaseNotes,
                localizationService.GetString("Common.Close")));
        }

        private void OnUpdateStateChanged(object? sender, ApplicationUpdateStateChangedEventArgs e)
        {
            _ = appDispatcher.TryEnqueue(() => ApplyCurrentUpdateState(e.CurrentResult));
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            _ = appDispatcher.TryEnqueue(() =>
            {
                CurrentVersion = localizationService.FormatString("Update.CurrentVersionFormat", FoundryApplicationInfo.Version);
                LastUpdateCheck = appSettingsService.Current.Updates.LastCheckedAt?.ToString("yyyy-MM-dd HH:mm")
                    ?? localizationService.GetString("Update.NotChecked");
                ApplyCurrentUpdateState(currentCheckResult);
            });
        }

        private void ApplyCurrentUpdateState(ApplicationUpdateCheckResult? result)
        {
            currentCheckResult = result;

            if (result is null)
            {
                LoadingStatus = localizationService.GetString("Update.Status.Ready");
                IsUpdateAvailable = false;
                IsInstallButtonVisible = false;
                IsReleaseNotesVisible = false;
                releaseNotes = localizationService.GetString("Update.ReleaseNotesFallback");
                return;
            }

            LoadingStatus = GetCheckStatusMessage(result);
            IsUpdateAvailable = result.IsUpdateAvailable;
            IsInstallButtonVisible = result.IsUpdateAvailable;
            IsReleaseNotesVisible = result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseNotes);
            releaseNotes = result.ReleaseNotes ?? localizationService.GetString("Update.NoReleaseNotes");
        }

        private string GetCheckStatusMessage(ApplicationUpdateCheckResult result)
        {
            return result.Status switch
            {
                ApplicationUpdateStatus.NoUpdate => localizationService.GetString("Update.Status.NoUpdate"),
                ApplicationUpdateStatus.UpdateAvailable => result.Version is not null
                    ? localizationService.FormatString("Update.Status.UpdateAvailableWithVersion", result.Version)
                    : localizationService.GetString("Update.Status.UpdateAvailable"),
                ApplicationUpdateStatus.Failed => localizationService.FormatString("Update.Status.FailedFormat", result.Message),
                ApplicationUpdateStatus.SkippedInDebug => localizationService.GetString("Update.Status.SkippedInDebug"),
                ApplicationUpdateStatus.NotInstalled => localizationService.GetString("Update.Status.NotInstalled"),
                _ => result.Message
            };
        }
    }
}
