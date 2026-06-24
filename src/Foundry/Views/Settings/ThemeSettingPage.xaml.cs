// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
