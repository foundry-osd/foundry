using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class StartPage : Page
{
    public StartPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_StartKey.Title"));
    }
}
