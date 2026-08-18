// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

public sealed partial class AutopilotInteractiveHashUploadPage : Page
{
    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotInteractiveHashUploadPage()
    {
        ViewModel = App.GetService<AutopilotConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    private async void OnModeActionClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleProvisioningModeAsync(AutopilotProvisioningMode.InteractiveHardwareHashUpload);
    }

}
