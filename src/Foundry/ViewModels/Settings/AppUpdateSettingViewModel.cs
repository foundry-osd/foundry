using Foundry.Core.Services.Application;
using Foundry.Services.Settings;

namespace Foundry.ViewModels
{
    public sealed partial class AppUpdateSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IDialogService dialogService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly string releaseNotes = "Release notes will be available from the configured Velopack update feed.";

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

        public AppUpdateSettingViewModel(
            IAppSettingsService appSettingsService,
            IDialogService dialogService,
            IExternalProcessLauncher externalProcessLauncher)
        {
            this.appSettingsService = appSettingsService;
            this.dialogService = dialogService;
            this.externalProcessLauncher = externalProcessLauncher;

            CurrentVersion = $"Current Version {FoundryApplicationInfo.Version}";
            LastUpdateCheck = "Not checked";
            IsCheckButtonEnabled = true;
            LoadingStatus = "Ready";
        }

        [RelayCommand]
        private async Task CheckForUpdateAsync()
        {
            IsLoading = true;
            IsUpdateAvailable = false;
            IsCheckButtonEnabled = false;

            try
            {
                LoadingStatus = "Velopack update checks will be enabled during the distribution phase.";
                LastUpdateCheck = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
                await Task.CompletedTask;
            }
            finally
            {
                IsLoading = false;
                IsCheckButtonEnabled = true;
            }
        }

        [RelayCommand]
        private Task GoToUpdateAsync()
        {
            return externalProcessLauncher.OpenUriAsync(new Uri(appSettingsService.Current.Updates.FeedUrl));
        }

        [RelayCommand]
        private Task GetReleaseNotesAsync()
        {
            return dialogService.ShowMessageAsync(new DialogRequest("Release Note", releaseNotes, "Close"));
        }
    }
}
