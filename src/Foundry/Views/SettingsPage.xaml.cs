using Foundry.Services.Localization;
using Foundry.Services.Shell;

namespace Foundry.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly IApplicationLocalizationService localizationService;
        private readonly IShellNavigationGuardService shellNavigationGuardService;

        public SettingsPage()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
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
                _ = DispatcherQueue.TryEnqueue(ApplyLocalizedText);
                return;
            }

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
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
            AboutSettingsCard.CommandParameter = CreateNavigationParameter(
                typeof(AboutUsSettingPage),
                "SettingsPage_AboutCard.Header");
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
                _ = DispatcherQueue.TryEnqueue(ApplyNavigationGuardState);
                return;
            }

            ApplyNavigationGuardState();
        }

        private void ApplyNavigationGuardState()
        {
            GeneralSettingsCard.IsEnabled = true;
            ThemeSettingsCard.IsEnabled = true;
            UpdateSettingsCard.IsEnabled = true;
            AboutSettingsCard.IsEnabled = true;
        }
    }

}
