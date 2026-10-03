// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.ViewModels
{
    /// <summary>
    /// Owns shell-level update footer state for the main window.
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
        private readonly ILogger logger;
        private ApplicationUpdateCheckResult? currentUpdateResult;
        private bool isDisposed;

        [ObservableProperty]
        public partial bool IsUpdateFooterItemVisible { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterTitle { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterToolTip { get; set; }

        [ObservableProperty]
        public partial int UpdateDownloadProgress { get; set; }

        [ObservableProperty]
        public partial bool IsUpdateDownloading { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterGlyph { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterAutomationName { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterItemStatus { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterHelpText { get; set; }

        /// <summary>
        /// Occurs once after a visible target changes lifecycle, excluding progress and localization refreshes.
        /// </summary>
        public event EventHandler<string>? UpdateFooterStatusChanged;

        public bool IsUpdateReadyToApply => currentUpdateResult?.IsReadyToApply == true;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainViewModel"/> class.
        /// </summary>
        public MainViewModel(
            IApplicationUpdateStateService updateStateService,
            IApplicationLocalizationService localizationService,
            IAppDispatcher appDispatcher,
            ILogger logger)
        {
            this.updateStateService = updateStateService;
            this.localizationService = localizationService;
            this.appDispatcher = appDispatcher;
            this.logger = logger.ForContext<MainViewModel>();

            UpdateFooterTitle = localizationService.GetString("UpdateFooter.Title");
            UpdateFooterToolTip = localizationService.GetString("Update.Status.UpdateAvailable");
            UpdateFooterGlyph = "\uEBD3";
            UpdateFooterAutomationName = UpdateFooterTitle;
            UpdateFooterItemStatus = UpdateFooterToolTip;
            UpdateFooterHelpText = UpdateFooterToolTip;

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyUpdateState(updateStateService.CurrentResult);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            isDisposed = true;
            updateStateService.StateChanged -= OnUpdateStateChanged;
            localizationService.LanguageChanged -= OnLanguageChanged;
        }

        private void OnUpdateStateChanged(object? sender, ApplicationUpdateStateChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() => ApplyUpdateState(e.CurrentResult, announceLifecycle: true)))
            {
                logger.Warning(
                    "Failed to enqueue update footer state refresh. Status={Status}, Version={Version}",
                    e.CurrentResult?.Status,
                    e.CurrentResult?.Version);
            }
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() => ApplyUpdateState(currentUpdateResult)))
            {
                logger.Warning(
                    "Failed to enqueue update footer localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                    e.OldLanguage,
                    e.NewLanguage);
            }
        }

        private void ApplyUpdateState(ApplicationUpdateCheckResult? result, bool announceLifecycle = false)
        {
            if (isDisposed)
            {
                return;
            }

            bool lifecycleChanged = currentUpdateResult?.Status != result?.Status
                || currentUpdateResult?.Version != result?.Version;
            currentUpdateResult = result;
            UpdateDownloadProgress = Math.Clamp(result?.DownloadProgress ?? 0, 0, 100);
            IsUpdateDownloading = result?.Status == ApplicationUpdateStatus.Downloading;
            UpdateFooterTitle = IsUpdateDownloading
                ? localizationService.FormatString("Update.Footer.DownloadingFormat", UpdateDownloadProgress)
                : localizationService.GetString(IsUpdateReadyToApply ? "Update.Action.Apply" : "UpdateFooter.Title");
            UpdateFooterGlyph = IsUpdateReadyToApply ? "\uE8FB" : "\uEBD3";
            UpdateFooterAutomationName = IsUpdateDownloading
                ? localizationService.GetString("Update.Status.Downloading")
                : UpdateFooterTitle;
            UpdateFooterToolTip = IsUpdateReadyToApply
                ? localizationService.GetString("Update.Footer.ReadyToolTip")
                : IsUpdateDownloading
                    ? UpdateFooterTitle
                    : result?.Version is not null
                        ? localizationService.FormatString("Update.Status.UpdateAvailableWithVersion", result.Version)
                        : localizationService.GetString("Update.Status.UpdateAvailable");
            UpdateFooterHelpText = IsUpdateDownloading
                ? localizationService.GetString("Update.Footer.DownloadingHelp")
                : UpdateFooterToolTip;
            UpdateFooterItemStatus = result?.Status == ApplicationUpdateStatus.Failed
                ? localizationService.FormatString("Update.Status.DownloadFailedFormat", result.FailureMessage ?? result.Message)
                : IsUpdateReadyToApply ? UpdateFooterTitle : UpdateFooterToolTip;
            IsUpdateFooterItemVisible = result?.HasKnownUpdate == true;
            OnPropertyChanged(nameof(IsUpdateReadyToApply));

            if (announceLifecycle && lifecycleChanged && IsUpdateFooterItemVisible)
            {
                UpdateFooterStatusChanged?.Invoke(this, UpdateFooterItemStatus);
            }
        }
    }
}
