using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Updates;

namespace Foundry.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IApplicationUpdateStateService updateStateService;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAppDispatcher appDispatcher;
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

        public MainViewModel(
            IApplicationUpdateStateService updateStateService,
            IApplicationLocalizationService localizationService,
            IAppDispatcher appDispatcher)
        {
            this.updateStateService = updateStateService;
            this.localizationService = localizationService;
            this.appDispatcher = appDispatcher;

            UpdateBannerTitle = localizationService.GetString("UpdateBanner.Title");
            UpdateBannerMessage = localizationService.GetString("Update.Status.UpdateAvailable");
            UpdateBannerActionText = localizationService.GetString("UpdateBanner.Action");

            updateStateService.StateChanged += OnUpdateStateChanged;
            localizationService.LanguageChanged += OnLanguageChanged;
            ApplyUpdateState(updateStateService.CurrentResult);
        }

        public void DismissUpdateBanner()
        {
            isUpdateBannerDismissed = true;
            IsUpdateBannerOpen = false;
        }

        public void MarkUpdateBannerActionOpened()
        {
            isUpdateBannerDismissed = true;
            IsUpdateBannerOpen = false;
        }

        public void Dispose()
        {
            updateStateService.StateChanged -= OnUpdateStateChanged;
            localizationService.LanguageChanged -= OnLanguageChanged;
        }

        private void OnUpdateStateChanged(object? sender, ApplicationUpdateStateChangedEventArgs e)
        {
            _ = appDispatcher.TryEnqueue(() => ApplyUpdateState(e.CurrentResult));
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            _ = appDispatcher.TryEnqueue(() => ApplyUpdateState(currentUpdateResult));
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
