using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class LocalizationPage : Page
{
    public LocalizationPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_LocalizationKey.Title"));
    }
}
