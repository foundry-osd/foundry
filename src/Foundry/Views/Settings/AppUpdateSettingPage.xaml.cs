namespace Foundry.Views
{
    /// <summary>
    /// Hosts the application update settings UI and update-related dialogs.
    /// </summary>
    public sealed partial class AppUpdateSettingPage : Page
    {
        /// <summary>
        /// Gets the update settings view model.
        /// </summary>
        public AppUpdateSettingViewModel ViewModel { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AppUpdateSettingPage"/> class.
        /// </summary>
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
