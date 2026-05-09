namespace Foundry.Views;

public sealed partial class AboutOverviewDialogPage : Page
{
    public AboutOverviewDialogPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);
    }
}
