using Foundry.Services.Localization;

namespace Foundry.Views;

public sealed partial class DocumentationPage : Page
{
    public DocumentationPage()
    {
        InitializeComponent();
        BreadcrumbNavigator.SetPageTitle(this, App.GetService<IApplicationLocalizationService>().GetString("Nav_DocumentationKey.Title"));
    }
}
