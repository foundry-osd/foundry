namespace Foundry.Views
{
    public sealed partial class ThemeSettingPage : Page
    {
        public ThemeSettingPage()
        {
            this.InitializeComponent();
        }

        private async void AccentButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:colors"));
        }
    }


}
