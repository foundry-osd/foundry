using System.Globalization;
using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.ViewModels
{
    /// <summary>
    /// Coordinates application update checks, release notes, and restart handoff state for the update settings page.
    /// </summary>
    public sealed partial class AppUpdateSettingViewModel : ObservableObject, IDisposable
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IDialogService dialogService;
        private readonly IApplicationUpdateService applicationUpdateService;
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
        private readonly ILogger logger;
        private ApplicationUpdateCheckResult? currentCheckResult;

        [ObservableProperty]
        public partial string InstalledVersion { get; set; }

        [ObservableProperty]
        public partial string AvailableVersion { get; set; }

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
        public partial string UpdateStatusTitle { get; set; }

        [ObservableProperty]
        public partial double DownloadProgress { get; set; }

        [ObservableProperty]
        public partial bool IsInstallButtonVisible { get; set; }

        [ObservableProperty]
        public partial bool IsReleaseNotesVisible { get; set; }

        public string UpdateFeedUrl => appSettingsService.Current.Updates.FeedUrl;
        public string UpdateSourceDescription => GetUpdateSourceDescription();
        public string UpdateSourceTitle => localizationService.GetString("AppUpdate.UpdateSourceTitle");
        public string InstalledVersionLabel => localizationService.GetString("Update.Field.InstalledVersion");
        public string AvailableVersionLabel => currentCheckResult?.Status == ApplicationUpdateStatus.NoUpdate
            ? localizationService.GetString("Update.Field.LatestVersion")
            : localizationService.GetString("Update.Field.AvailableVersion");
        public string LastUpdateCheckLabel => localizationService.GetString("Update.Field.LastUpdateCheck");
        public string UpdateNewBadgeText => localizationService.GetString("Update.Badge.New");
        public string CloseText => localizationService.GetString("Common.Close");
        public string ReleaseNotesLoadingText => localizationService.GetString("AboutDialog.ReleaseNotesLoading");
        public string ReleaseNotesErrorText => localizationService.GetString("AboutDialog.ReleaseNotesError");
        public string ReleaseNotesRepositoryText => localizationService.GetString("Update.ReleaseNotesRepository");
        public string InstallProgressDialogTitle => localizationService.GetString("Update.InstallProgressDialog.Title");
        public string InstallProgressDialogMessage => localizationService.GetString("Update.InstallProgressDialog.Message");
        public string InstallProgressDialogVersionText => localizationService.FormatString("Update.InstallProgressDialog.VersionFormat", AvailableVersion);
        public string DownloadProgressLabel => localizationService.GetString("Update.InstallProgressDialog.ProgressLabel");
        public string DownloadProgressText => string.Create(CultureInfo.CurrentCulture, $"{DownloadProgress:0.#}");
        public Uri ReleasesUri { get; } = new(FoundryApplicationInfo.ReleasesUrl);

        /// <summary>
        /// Initializes a new instance of the <see cref="AppUpdateSettingViewModel"/> class.
        /// </summary>
        public AppUpdateSettingViewModel(
            IAppSettingsService appSettingsService,
            IDialogService dialogService,
            IApplicationUpdateService applicationUpdateService,
            IApplicationUpdateStateService updateStateService,
            IApplicationLocalizationService localizationService,
            IAppDispatcher appDispatcher,
            ILogger logger)
        {
            this.appSettingsService = appSettingsService;
            this.dialogService = dialogService;
            this.applicationUpdateService = applicationUpdateService;
            this.updateStateService = updateStateService;
            this.localizationService = localizationService;
            this.appDispatcher = appDispatcher;
            this.logger = logger.ForContext<AppUpdateSettingViewModel>();

            InstalledVersion = FoundryApplicationInfo.Version;
            AvailableVersion = localizationService.GetString("Update.NotChecked");
            LastUpdateCheck = FormatLastUpdateCheck(appSettingsService.Current.Updates.LastCheckedAt);
            IsCheckButtonEnabled = true;
            LoadingStatus = localizationService.GetString("Update.Status.Ready");
            UpdateStatusTitle = localizationService.GetString("Update.Status.Ready");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyCurrentUpdateState(updateStateService.CurrentResult);
        }

        /// <inheritdoc />
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

                LastUpdateCheck = FormatLastUpdateCheck(checkedAt);
                ApplyCurrentUpdateState(result);
            }
            finally
            {
                IsLoading = false;
                IsCheckButtonEnabled = true;
            }
        }

        /// <summary>
        /// Shows the restart confirmation before downloading and applying an available update.
        /// </summary>
        /// <returns><see langword="true"/> when the user confirms the update and restart operation.</returns>
        public Task<bool> ConfirmDownloadAndRestartUpdateAsync()
        {
            return dialogService.ConfirmAsync(new ConfirmationDialogRequest(
                localizationService.GetString("Update.ConfirmDownloadRestart.Title"),
                localizationService.GetString("Update.ConfirmDownloadRestart.Message"),
                localizationService.GetString("Update.ConfirmDownloadRestart.PrimaryButton"),
                localizationService.GetString("Common.Cancel"),
                IsPrimaryButtonAccent: true));
        }

        /// <summary>
        /// Downloads the available update, reports progress, and starts the Velopack restart handoff when ready.
        /// </summary>
        public async Task DownloadAndRestartUpdateAsync()
        {
            IsLoading = true;
            IsCheckButtonEnabled = false;
            IsInstallButtonVisible = false;
            DownloadProgress = 0;

            try
            {
                Progress<int> progress = new(value =>
                {
                    SetDownloadProgressTarget(value);
                });

                ApplicationUpdateDownloadResult result = await applicationUpdateService.DownloadUpdateAsync(progress);
                LoadingStatus = result.Message;

                if (result.Status == ApplicationUpdateStatus.ReadyToRestart)
                {
                    DownloadProgress = 100;
                    await Task.Delay(TimeSpan.FromMilliseconds(300));
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

        private void OnUpdateStateChanged(object? sender, ApplicationUpdateStateChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() => ApplyCurrentUpdateState(e.CurrentResult)))
            {
                logger.Warning(
                    "Failed to enqueue update settings state refresh. Status={Status}, Version={Version}",
                    e.CurrentResult?.Status,
                    e.CurrentResult?.Version);
            }
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() =>
            {
                InstalledVersion = FoundryApplicationInfo.Version;
                LastUpdateCheck = FormatLastUpdateCheck(appSettingsService.Current.Updates.LastCheckedAt);
                OnPropertyChanged(nameof(UpdateSourceDescription));
                OnPropertyChanged(nameof(UpdateSourceTitle));
                OnPropertyChanged(nameof(InstalledVersionLabel));
                OnPropertyChanged(nameof(AvailableVersionLabel));
                OnPropertyChanged(nameof(LastUpdateCheckLabel));
                OnPropertyChanged(nameof(UpdateNewBadgeText));
                OnPropertyChanged(nameof(CloseText));
                OnPropertyChanged(nameof(ReleaseNotesLoadingText));
                OnPropertyChanged(nameof(ReleaseNotesErrorText));
                OnPropertyChanged(nameof(ReleaseNotesRepositoryText));
                OnPropertyChanged(nameof(InstallProgressDialogTitle));
                OnPropertyChanged(nameof(InstallProgressDialogMessage));
                OnPropertyChanged(nameof(InstallProgressDialogVersionText));
                OnPropertyChanged(nameof(DownloadProgressLabel));
                OnPropertyChanged(nameof(DownloadProgressText));
                ApplyCurrentUpdateState(currentCheckResult);
            }))
            {
                logger.Warning(
                    "Failed to enqueue update settings localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                    e.OldLanguage,
                    e.NewLanguage);
            }
        }

        private void ApplyCurrentUpdateState(ApplicationUpdateCheckResult? result)
        {
            currentCheckResult = result;

            if (result is null)
            {
                LoadingStatus = localizationService.GetString("Update.Status.Ready");
                UpdateStatusTitle = localizationService.GetString("Update.Status.Ready");
                AvailableVersion = localizationService.GetString("Update.NotChecked");
                IsUpdateAvailable = false;
                IsInstallButtonVisible = false;
                IsReleaseNotesVisible = false;
                OnPropertyChanged(nameof(AvailableVersionLabel));
                return;
            }

            LoadingStatus = GetCheckStatusMessage(result);
            UpdateStatusTitle = GetCheckStatusTitle(result);
            AvailableVersion = GetAvailableVersion(result);
            OnPropertyChanged(nameof(InstallProgressDialogVersionText));
            IsUpdateAvailable = result.IsUpdateAvailable;
            IsInstallButtonVisible = result.IsUpdateAvailable;
            IsReleaseNotesVisible = result.IsUpdateAvailable;
            OnPropertyChanged(nameof(AvailableVersionLabel));
        }

        private string GetAvailableVersion(ApplicationUpdateCheckResult result)
        {
            if (result.Status == ApplicationUpdateStatus.UpdateAvailable && result.Version is not null)
            {
                return result.Version.ToString();
            }

            if (result.Status == ApplicationUpdateStatus.NoUpdate)
            {
                return InstalledVersion;
            }

            return localizationService.GetString("Update.NotChecked");
        }

        private string GetCheckStatusMessage(ApplicationUpdateCheckResult result)
        {
            return result.Status switch
            {
                ApplicationUpdateStatus.NoUpdate => localizationService.GetString("Update.Status.NoUpdate"),
                ApplicationUpdateStatus.UpdateAvailable => localizationService.GetString("Update.Status.UpdateAvailableActionHint"),
                ApplicationUpdateStatus.Failed => localizationService.FormatString("Update.Status.FailedFormat", result.Message),
                ApplicationUpdateStatus.SkippedInDebug => localizationService.GetString("Update.Status.SkippedInDebug"),
                ApplicationUpdateStatus.NotInstalled => localizationService.GetString("Update.Status.NotInstalled"),
                _ => result.Message
            };
        }

        private string GetCheckStatusTitle(ApplicationUpdateCheckResult result)
        {
            return result.Status switch
            {
                ApplicationUpdateStatus.NoUpdate => localizationService.GetString("Update.StatusTitle.NoUpdate"),
                ApplicationUpdateStatus.UpdateAvailable when result.Version is not null =>
                    localizationService.FormatString("Update.StatusTitle.UpdateAvailableFormat", result.Version),
                ApplicationUpdateStatus.UpdateAvailable => localizationService.GetString("Update.StatusTitle.UpdateAvailable"),
                ApplicationUpdateStatus.Failed => localizationService.GetString("Update.StatusTitle.Failed"),
                ApplicationUpdateStatus.SkippedInDebug => localizationService.GetString("Update.StatusTitle.Skipped"),
                ApplicationUpdateStatus.NotInstalled => localizationService.GetString("Update.StatusTitle.Skipped"),
                _ => localizationService.GetString("Update.Status.Ready")
            };
        }

        private string GetUpdateSourceDescription()
        {
            if (!IsGitHubRepositoryUrl(UpdateFeedUrl))
            {
                return localizationService.GetString("AppUpdate.SourceKind.SimpleWeb");
            }

            return localizationService.FormatString(
                "AppUpdate.ReleaseLaneFormat",
                GetLocalizedReleaseLane(appSettingsService.Current.Updates.Channel));
        }

        private string GetLocalizedReleaseLane(string? channel)
        {
            return channel?.Trim().ToLowerInvariant() switch
            {
                "beta" => localizationService.GetString("AppUpdate.ReleaseLane.Beta"),
                "preview" => localizationService.GetString("AppUpdate.ReleaseLane.Preview"),
                "prerelease" => localizationService.GetString("AppUpdate.ReleaseLane.Preview"),
                _ => localizationService.GetString("AppUpdate.ReleaseLane.Stable")
            };
        }

        private static bool IsGitHubRepositoryUrl(string feedUrl)
        {
            return Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri)
                && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.Count(character => character == '/') == 2;
        }

        private string FormatLastUpdateCheck(DateTimeOffset? checkedAt)
        {
            return checkedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                ?? localizationService.GetString("Update.NotChecked");
        }

        partial void OnDownloadProgressChanged(double value)
        {
            OnPropertyChanged(nameof(DownloadProgressText));
        }

        private void SetDownloadProgressTarget(double target)
        {
            target = Math.Clamp(Math.Round(target, 1), 0d, 100d);
            if (target <= DownloadProgress)
            {
                return;
            }

            DownloadProgress = target;
        }
    }
}
