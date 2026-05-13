using Foundry.ViewModels;
using Foundry.Services.Localization;
using Foundry.Services.Shell;
using Serilog;

namespace Foundry.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly IApplicationLocalizationService localizationService;
        private readonly IShellNavigationGuardService shellNavigationGuardService;
        private readonly ILogger logger = Log.ForContext<SettingsPage>();

        public SettingsPageViewModel ViewModel { get; }

        public SettingsPage()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
            ViewModel = App.GetService<SettingsPageViewModel>();
            this.InitializeComponent();
            ApplyLocalizedText();
            localizationService.LanguageChanged += OnLanguageChanged;
            shellNavigationGuardService.StateChanged += OnShellNavigationStateChanged;
            Unloaded += OnUnloaded;
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                if (!DispatcherQueue.TryEnqueue(ApplyLocalizedText))
                {
                    logger.Warning(
                        "Failed to enqueue settings localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                        e.OldLanguage,
                        e.NewLanguage);
                }

                return;
            }

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            TelemetryCard.Header = localizationService.GetString("SettingsPage_TelemetryCard.Header");
            TelemetryCard.Description = localizationService.GetString("SettingsPage_TelemetryCard.Description");
            TelemetryToggle.OnContent = localizationService.GetString("Common.Enabled");
            TelemetryToggle.OffContent = localizationService.GetString("Common.Disabled");
            ApplyLocalizedNavigationParameters();
            ApplyNavigationGuardState();
        }

        private void ApplyLocalizedNavigationParameters()
        {
            GeneralSettingsCard.CommandParameter = CreateNavigationParameter(
                typeof(GeneralSettingPage),
                "SettingsPage_GeneralCard.Header");
            ThemeSettingsCard.CommandParameter = CreateNavigationParameter(
                typeof(ThemeSettingPage),
                "SettingsPage_ThemeCard.Header");
            UpdateSettingsCard.CommandParameter = CreateNavigationParameter(
                typeof(AppUpdateSettingPage),
                "SettingsPage_UpdateCard.Header");
        }

        private NavigationParameterExtension CreateNavigationParameter(Type pageType, string titleResourceKey)
        {
            return new NavigationParameterExtension
            {
                PageType = pageType,
                BreadCrumbHeader = localizationService.GetString(titleResourceKey)
            };
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            localizationService.LanguageChanged -= OnLanguageChanged;
            shellNavigationGuardService.StateChanged -= OnShellNavigationStateChanged;
            Unloaded -= OnUnloaded;
        }

        private void OnShellNavigationStateChanged(object? sender, EventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                if (!DispatcherQueue.TryEnqueue(ApplyNavigationGuardState))
                {
                    logger.Warning(
                        "Failed to enqueue settings navigation state refresh. State={State}",
                        shellNavigationGuardService.State);
                }

                return;
            }

            ApplyNavigationGuardState();
        }

        private void ApplyNavigationGuardState()
        {
            GeneralSettingsCard.IsEnabled = true;
            ThemeSettingsCard.IsEnabled = true;
            UpdateSettingsCard.IsEnabled = true;
        }
    }

}
