using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class AutopilotPage : Page
{
    public AutopilotPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_AutopilotKey.Title"));
    }
}
