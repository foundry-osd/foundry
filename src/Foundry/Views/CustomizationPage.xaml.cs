using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class CustomizationPage : Page
{
    public CustomizationPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_CustomizationKey.Title"));
    }
}
