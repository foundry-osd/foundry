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
    }

}
