namespace Foundry.Views;

public sealed partial class AboutContributorsDialogPage : Page
{
    public AboutContributorsDialogPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);

        if (DataContext is AboutUsSettingViewModel viewModel)
        {
            await viewModel.LoadContributorsAsync();
        }
    }
}
