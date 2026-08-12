// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class CustomizationPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public CustomizationPage()
    {
        ViewModel = App.GetService<CustomizationConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    private void OnAppxRemovalProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AppxRemovalCategoryViewModel category })
        {
            ViewModel.ToggleAppxRemovalProfile(category);
        }
    }
}
