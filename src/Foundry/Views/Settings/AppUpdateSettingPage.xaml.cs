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

        private async void ReleaseNotesLink_Click(object sender, RoutedEventArgs e)
        {
            UpdateReleaseNotesDialog dialog = new(ViewModel)
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };

            await dialog.ShowAsync();
        }

        private async void DownloadRestartButton_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ViewModel.ConfirmDownloadAndRestartUpdateAsync();
            if (!confirmed)
            {
                return;
            }

            UpdateInstallProgressDialog dialog = new(ViewModel)
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };

            Task<ContentDialogResult> dialogTask = dialog.ShowAsync().AsTask();

            try
            {
                await ViewModel.DownloadAndRestartUpdateAsync();
            }
            finally
            {
                dialog.Hide();
                await dialogTask;
            }
        }
    }
}
