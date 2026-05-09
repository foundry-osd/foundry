namespace Foundry.Views;

public sealed partial class AboutLicensesDialogPage : Page
{
    public AboutLicensesDialogPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);
    }
}
