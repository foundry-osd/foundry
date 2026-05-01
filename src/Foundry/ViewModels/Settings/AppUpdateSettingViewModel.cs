using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Updates;

namespace Foundry.ViewModels
{
    public sealed partial class AppUpdateSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IDialogService dialogService;
        private readonly IApplicationUpdateService applicationUpdateService;
        private readonly IApplicationLocalizationService localizationService;
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
            IApplicationLocalizationService localizationService)
        {
            this.appSettingsService = appSettingsService;
            this.dialogService = dialogService;
            this.applicationUpdateService = applicationUpdateService;
            this.localizationService = localizationService;
            releaseNotes = localizationService.GetString("Update.ReleaseNotesFallback");

            CurrentVersion = localizationService.FormatString("Update.CurrentVersionFormat", FoundryApplicationInfo.Version);
            LastUpdateCheck = appSettingsService.Current.Updates.LastCheckedAt?.ToString("yyyy-MM-dd HH:mm") ?? localizationService.GetString("Update.NotChecked");
            IsCheckButtonEnabled = true;
            LoadingStatus = localizationService.GetString("Update.Status.Ready");
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
                LoadingStatus = GetCheckStatusMessage(result);
                IsUpdateAvailable = result.IsUpdateAvailable;
                IsInstallButtonVisible = result.IsUpdateAvailable;
                IsReleaseNotesVisible = result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseNotes);
                releaseNotes = result.ReleaseNotes ?? localizationService.GetString("Update.NoReleaseNotes");
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
