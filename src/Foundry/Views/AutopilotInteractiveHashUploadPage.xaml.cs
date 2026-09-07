// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class AutopilotInteractiveHashUploadPage : Page
{
    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotInteractiveHashUploadPage()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
        ViewModel = App.GetService<PageViewModelFactory>().Create<AutopilotConfigurationViewModel>();
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

    private void OnInteractiveContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        InteractiveImage.MaxHeight = Math.Max(
            0,
            InteractiveContentScrollView.ActualHeight
                - InteractiveDescription.ActualHeight
                - InteractivePresentation.RowSpacing);
    }

}
