using Foundry.Core.Services.Application;
using Foundry.Services.Settings;
using Foundry.Services.Updates;

namespace Foundry.ViewModels
{
    public sealed partial class AppUpdateSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IDialogService dialogService;
        private readonly IApplicationUpdateService applicationUpdateService;
        private string releaseNotes = "Release notes will be available from the configured Velopack update feed.";

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
            IApplicationUpdateService applicationUpdateService)
        {
            this.appSettingsService = appSettingsService;
            this.dialogService = dialogService;
            this.applicationUpdateService = applicationUpdateService;

            CurrentVersion = $"Current Version {FoundryApplicationInfo.Version}";
            LastUpdateCheck = appSettingsService.Current.Updates.LastCheckedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Not checked";
            IsCheckButtonEnabled = true;
            LoadingStatus = "Ready";
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
            LoadingStatus = "Checking for updates";

            try
            {
                ApplicationUpdateCheckResult result = await applicationUpdateService.CheckForUpdatesAsync();
                DateTimeOffset checkedAt = DateTimeOffset.Now;
                appSettingsService.Current.Updates.LastCheckedAt = checkedAt;
                appSettingsService.Save();

                LastUpdateCheck = checkedAt.ToString("yyyy-MM-dd HH:mm");
                LoadingStatus = result.Message;
                IsUpdateAvailable = result.IsUpdateAvailable;
                IsInstallButtonVisible = result.IsUpdateAvailable;
                IsReleaseNotesVisible = result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseNotes);
                releaseNotes = result.ReleaseNotes ?? "No release notes were provided for this update.";

                if (result.Version is not null)
                {
                    LoadingStatus = $"{result.Message} Version {result.Version}.";
                }
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
            LoadingStatus = "Downloading update";
            DownloadProgress = 0;

            try
            {
                Progress<int> progress = new(value =>
                {
                    DownloadProgress = value;
                    LoadingStatus = $"Downloading update ({value}%)";
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
            return dialogService.ShowMessageAsync(new DialogRequest("Release Note", releaseNotes, "Close"));
        }
    }
}
