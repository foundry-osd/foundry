using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class GeneralConfigurationPage : Page
{
    public GeneralConfigurationPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_GeneralConfigurationKey.Title"));
    }
}
