namespace Foundry.Views
{
    public sealed partial class HomeLandingPage : Page
    {
        public HomeLandingViewModel ViewModel { get; }

        public HomeLandingPage()
        {
            ViewModel = App.GetService<HomeLandingViewModel>();
            this.InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshAdkStatusCommand.ExecuteAsync(null);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            ViewModel.Dispose();
        }

        private async void ActionGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is HomeActionItem action)
            {
                await ViewModel.ExecuteActionAsync(action);
            }
        }
    }
}
