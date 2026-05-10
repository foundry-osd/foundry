namespace Foundry.Views
{
    public sealed partial class AppUpdateSettingPage : Page
    {
        public AppUpdateSettingViewModel ViewModel { get; }

        public AppUpdateSettingPage()
        {
            ViewModel = App.GetService<AppUpdateSettingViewModel>();
            this.InitializeComponent();
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Dispose();
            Unloaded -= OnUnloaded;
        }
    }
}
