using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.ViewModels
{
    /// <summary>
    /// Owns shell-level update banner state for the main window.
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
        private readonly ILogger logger;
        private ApplicationUpdateCheckResult? currentUpdateResult;
        private bool isUpdateBannerDismissed;

        [ObservableProperty]
        public partial bool IsUpdateBannerOpen { get; set; }

        [ObservableProperty]
        public partial string UpdateBannerTitle { get; set; }

        [ObservableProperty]
        public partial string UpdateBannerMessage { get; set; }

        [ObservableProperty]
        public partial string UpdateBannerActionText { get; set; }

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

            UpdateBannerTitle = localizationService.GetString("UpdateBanner.Title");
            UpdateBannerMessage = localizationService.GetString("Update.Status.UpdateAvailable");
            UpdateBannerActionText = localizationService.GetString("UpdateBanner.Action");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyUpdateState(updateStateService.CurrentResult);
        }

        /// <summary>
        /// Hides the update banner until a different update result is published.
        /// </summary>
        public void DismissUpdateBanner()
        {
            isUpdateBannerDismissed = true;
            IsUpdateBannerOpen = false;
        }

        /// <summary>
        /// Marks the update banner action as handled and hides the banner.
        /// </summary>
        public void MarkUpdateBannerActionOpened()
        {
            isUpdateBannerDismissed = true;
            IsUpdateBannerOpen = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            updateStateService.StateChanged -= OnUpdateStateChanged;
            localizationService.LanguageChanged -= OnLanguageChanged;
        }

        private void OnUpdateStateChanged(object? sender, ApplicationUpdateStateChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() => ApplyUpdateState(e.CurrentResult)))
            {
                logger.Warning(
                    "Failed to enqueue update banner state refresh. Status={Status}, Version={Version}",
                    e.CurrentResult?.Status,
                    e.CurrentResult?.Version);
            }
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!appDispatcher.TryEnqueue(() => ApplyUpdateState(currentUpdateResult)))
            {
                logger.Warning(
                    "Failed to enqueue update banner localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                    e.OldLanguage,
                    e.NewLanguage);
            }
        }

        private void ApplyUpdateState(ApplicationUpdateCheckResult? result)
        {
            bool updateChanged = currentUpdateResult?.Status != result?.Status
                || !string.Equals(currentUpdateResult?.Version, result?.Version, StringComparison.Ordinal);

            currentUpdateResult = result;
            UpdateBannerTitle = localizationService.GetString("UpdateBanner.Title");
            UpdateBannerActionText = localizationService.GetString("UpdateBanner.Action");

            if (result?.IsUpdateAvailable == true)
            {
                if (updateChanged)
                {
                    isUpdateBannerDismissed = false;
                }

                UpdateBannerMessage = result.Version is not null
                    ? localizationService.FormatString("Update.Status.UpdateAvailableWithVersion", result.Version)
                    : localizationService.GetString("Update.Status.UpdateAvailable");

                IsUpdateBannerOpen = !isUpdateBannerDismissed;
                return;
            }

            isUpdateBannerDismissed = false;
            UpdateBannerMessage = localizationService.GetString("Update.Status.UpdateAvailable");
            IsUpdateBannerOpen = false;
        }
    }
}
