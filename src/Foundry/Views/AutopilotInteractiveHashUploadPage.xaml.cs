// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

public sealed partial class AutopilotInteractiveHashUploadPage : Page
{
    private const double MinimumInteractiveImageHeight = 240;

    public AutopilotConfigurationViewModel ViewModel { get; }

    /// <summary>Gets the read-only state of a managed configuration.</summary>
    public ManagedConfigurationViewModel Managed { get; } = App.GetService<ManagedConfigurationViewModel>();

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

    private void OnInteractiveContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The image gets the space left below the option cards; below the minimum the page scrolls instead.
        double availableHeight = InteractiveContentScrollView.ActualHeight
            - InteractiveOptions.ActualHeight
            - InteractiveLayout.RowSpacing
            - InteractiveDescription.ActualHeight
            - InteractivePresentation.RowSpacing;

        InteractiveImage.MaxHeight = Math.Max(MinimumInteractiveImageHeight, availableHeight);
    }
}
