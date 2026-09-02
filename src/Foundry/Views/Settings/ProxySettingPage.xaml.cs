// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Settings;
using Foundry.ViewModels;

namespace Foundry.Views;

public sealed partial class ProxySettingPage : Page
{
    private bool isInitializing = true;

    public ProxySettingViewModel ViewModel { get; }

    public ProxySettingPage()
    {
        ViewModel = App.GetService<ProxySettingViewModel>();
        InitializeComponent();
        SelectItem(MethodComboBox, ViewModel.Method.ToString());
        SelectItem(AuthenticationComboBox, ViewModel.AuthenticationMode.ToString());
        PortNumberBox.Value = ViewModel.Port;
        PasswordBox.Password = ViewModel.Password;
        isInitializing = false;
        UpdateVisibility();
    }

    private void MethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isInitializing && MethodComboBox.SelectedItem is ComboBoxItem { Tag: string value } && Enum.TryParse(value, true, out ProxyMethod method))
        {
            ViewModel.Method = method;
            UpdateVisibility();
        }
    }

    private void AuthenticationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isInitializing && AuthenticationComboBox.SelectedItem is ComboBoxItem { Tag: string value } && Enum.TryParse(value, true, out ProxyAuthenticationMode mode))
        {
            ViewModel.AuthenticationMode = mode;
            UpdateVisibility();
        }
    }

    private void PortNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!isInitializing && !double.IsNaN(args.NewValue))
        {
            ViewModel.Port = (int)args.NewValue;
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
        ViewModel.Apply();
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
        TestButton.IsEnabled = false;
        try
        {
            await ViewModel.TestConnectionAsync();
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void UpdateVisibility()
    {
        ManualSettingsPanel.Visibility = ViewModel.Method == ProxyMethod.Manual ? Visibility.Visible : Visibility.Collapsed;
        CredentialsCard.Visibility = ViewModel.AuthenticationMode == ProxyAuthenticationMode.Explicit ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SelectItem(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.Ordinal));
    }
}
