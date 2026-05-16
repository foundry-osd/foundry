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

        [ObservableProperty]
        public partial bool IsUpdateFooterItemVisible { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterTitle { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterToolTip { get; set; }

        [ObservableProperty]
        public partial string UpdateFooterBadgeValue { get; set; }

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
            UpdateFooterBadgeValue = localizationService.GetString("UpdateFooter.BadgeFallback");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyUpdateState(updateStateService.CurrentResult);
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
            currentUpdateResult = result;
            UpdateFooterTitle = localizationService.GetString("UpdateFooter.Title");

            if (result?.IsUpdateAvailable == true)
            {
                UpdateFooterToolTip = result.Version is not null
                    ? localizationService.FormatString("Update.Status.UpdateAvailableWithVersion", result.Version)
                    : localizationService.GetString("Update.Status.UpdateAvailable");

                UpdateFooterBadgeValue = localizationService.GetString("UpdateFooter.BadgeFallback");
                IsUpdateFooterItemVisible = true;
                return;
            }

            UpdateFooterToolTip = localizationService.GetString("Update.Status.UpdateAvailable");
            UpdateFooterBadgeValue = localizationService.GetString("UpdateFooter.BadgeFallback");
            IsUpdateFooterItemVisible = false;
        }
    }
}
