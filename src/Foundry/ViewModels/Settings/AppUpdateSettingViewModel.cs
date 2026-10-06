// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.ViewModels
{
    /// <summary>
    /// Coordinates application update checks, downloads, release notes link visibility, and apply handoff state for the update settings page.
    /// </summary>
    public sealed partial class AppUpdateSettingViewModel : ObservableObject, IDisposable
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IApplicationUpdateRestartService updateRestartService;
        private readonly IApplicationUpdateService applicationUpdateService;
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
        private readonly ILogger logger;
        private ApplicationUpdateCheckResult? currentCheckResult;
        private bool isDisposed;

        [ObservableProperty]
        public partial string InstalledVersion { get; set; }

        [ObservableProperty]
        public partial string AvailableVersion { get; set; }

        [ObservableProperty]
        public partial string LastUpdateCheck { get; set; }

        [ObservableProperty]
        public partial bool IsUpdateAvailable { get; set; }

        [ObservableProperty]
        public partial bool IsCheckButtonEnabled { get; set; }

        [ObservableProperty]
        public partial string LoadingStatus { get; set; }

        [ObservableProperty]
        public partial string UpdateStatusTitle { get; set; }

        [ObservableProperty]
        public partial int DownloadProgress { get; set; }

        [ObservableProperty]
        public partial bool IsDownloadButtonVisible { get; set; }

        [ObservableProperty]
        public partial bool IsApplyButtonVisible { get; set; }

        [ObservableProperty]
        public partial bool IsUpdateDownloading { get; set; }

        [ObservableProperty]
        public partial string DownloadActionText { get; set; }

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
        public string ApplyActionText => localizationService.GetString("Update.Action.Apply");
        public string DownloadProgressText => localizationService.FormatString("Update.Footer.DownloadingFormat", DownloadProgress);

        /// <summary>
        /// Initializes a new instance of the <see cref="AppUpdateSettingViewModel"/> class.
        /// </summary>
        public AppUpdateSettingViewModel(
            IAppSettingsService appSettingsService,
            IApplicationUpdateRestartService updateRestartService,
            IApplicationUpdateService applicationUpdateService,
            IApplicationUpdateStateService updateStateService,
            IApplicationLocalizationService localizationService,
            IAppDispatcher appDispatcher,
            ILogger logger)
        {
            this.appSettingsService = appSettingsService;
            this.updateRestartService = updateRestartService;
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
            DownloadActionText = localizationService.GetString("Update.Action.Download");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyCurrentUpdateState(updateStateService.CurrentResult);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            isDisposed = true;
            updateStateService.StateChanged -= OnUpdateStateChanged;
            localizationService.LanguageChanged -= OnLanguageChanged;
        }

        [RelayCommand]
        private async Task CheckForUpdateAsync()
        {
            await applicationUpdateService.CheckForUpdatesAsync();
        }

        /// <summary>
        /// Prepares the shared target without requesting application restart.
        /// </summary>
        [RelayCommand]
        public async Task DownloadUpdateAsync()
        {
            await applicationUpdateService.DownloadUpdateAsync();
        }

        /// <summary>
        /// Applies the prepared target through the protected save and close workflow.
        /// </summary>
        [RelayCommand]
        public async Task ApplyUpdateAsync()
        {
            await updateRestartService.ApplyUpdateAsync();
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
                if (isDisposed)
                {
                    return;
                }

                InstalledVersion = FoundryApplicationInfo.Version;
                LastUpdateCheck = FormatLastUpdateCheck(appSettingsService.Current.Updates.LastCheckedAt);
                OnPropertyChanged(nameof(UpdateSourceDescription));
                OnPropertyChanged(nameof(UpdateSourceTitle));
                OnPropertyChanged(nameof(InstalledVersionLabel));
                OnPropertyChanged(nameof(AvailableVersionLabel));
                OnPropertyChanged(nameof(LastUpdateCheckLabel));
                OnPropertyChanged(nameof(UpdateNewBadgeText));
                OnPropertyChanged(nameof(ApplyActionText));
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
            if (isDisposed)
            {
                return;
            }

            currentCheckResult = result;
            LastUpdateCheck = FormatLastUpdateCheck(appSettingsService.Current.Updates.LastCheckedAt);
            bool isBusy = result?.IsBusy == true;
            IsUpdateDownloading = result?.Status == ApplicationUpdateStatus.Downloading;
            IsCheckButtonEnabled = !isBusy && result?.IsReadyToApply != true;
            DownloadProgress = Math.Clamp(result?.DownloadProgress ?? 0, 0, 100);
            IsDownloadButtonVisible = result?.HasKnownUpdate == true && !isBusy && !result.IsReadyToApply;
            IsApplyButtonVisible = result?.IsReadyToApply == true;
            DownloadActionText = localizationService.GetString(result?.Status == ApplicationUpdateStatus.Failed
                ? "Update.Action.Retry" : "Update.Action.Download");

            if (result is null)
            {
                LoadingStatus = localizationService.GetString("Update.Status.Ready");
                UpdateStatusTitle = localizationService.GetString("Update.Status.Ready");
                AvailableVersion = localizationService.GetString("Update.NotChecked");
                IsUpdateAvailable = false;
                IsReleaseNotesVisible = false;
                OnPropertyChanged(nameof(AvailableVersionLabel));
                return;
            }

            LoadingStatus = GetCheckStatusMessage(result);
            UpdateStatusTitle = GetCheckStatusTitle(result);
            AvailableVersion = GetAvailableVersion(result);
            IsUpdateAvailable = result.HasKnownUpdate;
            IsReleaseNotesVisible = result.HasKnownUpdate;
            OnPropertyChanged(nameof(AvailableVersionLabel));
        }

        private string GetAvailableVersion(ApplicationUpdateCheckResult result)
        {
            if (result.HasKnownUpdate && result.Version is not null)
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
                ApplicationUpdateStatus.UpdateAvailable when result.FailureMessage is not null =>
                    localizationService.FormatString("Update.Status.FailedFormat", result.FailureMessage),
                ApplicationUpdateStatus.UpdateAvailable => localizationService.GetString("Update.Status.UpdateAvailableActionHint"),
                ApplicationUpdateStatus.Checking => localizationService.GetString("Update.Status.Checking"),
                ApplicationUpdateStatus.Downloading => DownloadProgressText,
                ApplicationUpdateStatus.ReadyToApply => localizationService.GetString("Update.Footer.ReadyToolTip"),
                ApplicationUpdateStatus.Failed when result.HasKnownUpdate => localizationService.FormatString("Update.Status.DownloadFailedFormat", result.FailureMessage ?? result.Message),
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
                ApplicationUpdateStatus.Checking => localizationService.GetString("Update.Status.Checking"),
                ApplicationUpdateStatus.Downloading => localizationService.GetString("Update.Status.Downloading"),
                ApplicationUpdateStatus.ReadyToApply => localizationService.GetString("Update.Action.Apply"),
                ApplicationUpdateStatus.Failed when result.HasKnownUpdate => localizationService.GetString("Update.Action.Retry"),
                ApplicationUpdateStatus.UpdateAvailable when result.FailureMessage is not null => localizationService.GetString("Update.StatusTitle.Failed"),
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

        partial void OnDownloadProgressChanged(int value)
        {
            OnPropertyChanged(nameof(DownloadProgressText));
        }

    }
}
