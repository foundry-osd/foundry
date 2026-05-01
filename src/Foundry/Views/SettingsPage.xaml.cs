using Foundry.Services.Localization;

namespace Foundry.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly IApplicationLocalizationService localizationService;

        public SettingsPage()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            this.InitializeComponent();
            ApplyLocalizedText();
            localizationService.LanguageChanged += OnLanguageChanged;
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
            BreadcrumbNavigator.SetPageTitle(this, localizationService.GetString("SettingsPage.PageTitle"));
            ApplyLocalizedNavigationParameters();
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
            Unloaded -= OnUnloaded;
        }
    }

}
