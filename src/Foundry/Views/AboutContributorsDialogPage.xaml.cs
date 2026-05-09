namespace Foundry.Views;

public sealed partial class AboutContributorsDialogPage : Page
{
    public AboutContributorsDialogPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);
    }
}
