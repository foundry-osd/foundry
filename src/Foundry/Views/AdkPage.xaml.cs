using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class AdkPage : Page
{
    public AdkPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_AdkKey.Title"));
    }
}
