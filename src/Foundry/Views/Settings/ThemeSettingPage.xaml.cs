// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Appearance;

namespace Foundry.Views
{
    public sealed partial class ThemeSettingPage : Page
    {
        private readonly IAppThemeService themeService;
        private bool isInitializingSelection = true;

        public ThemeSettingPage()
        {
            themeService = App.GetService<IAppThemeService>();
            this.InitializeComponent();
            SelectItem(ElementThemeComboBox, themeService.ElementTheme.ToString());
            SelectItem(BackdropComboBox, themeService.Backdrop.ToString());
            isInitializingSelection = false;
        }

        private void ElementThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitializingSelection &&
                ElementThemeComboBox.SelectedItem is ComboBoxItem { Tag: string value } &&
                Enum.TryParse(value, ignoreCase: true, out ElementTheme theme))
            {
                themeService.SetElementTheme(theme);
            }
        }

        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitializingSelection &&
                BackdropComboBox.SelectedItem is ComboBoxItem { Tag: string value } &&
                Enum.TryParse(value, ignoreCase: true, out AppBackdropKind backdrop))
            {
                themeService.SetBackdrop(backdrop);
            }
        }

        private static void SelectItem(ComboBox comboBox, string value)
        {
            comboBox.SelectedItem = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.Ordinal));
        }

        private async void AccentButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:colors"));
        }
    }


}
