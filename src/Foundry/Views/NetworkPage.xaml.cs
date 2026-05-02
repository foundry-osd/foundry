using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_NetworkKey.Title"));
    }
}
