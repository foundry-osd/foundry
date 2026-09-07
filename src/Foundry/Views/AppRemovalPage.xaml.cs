// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class AppRemovalPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public AppRemovalPage()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
        ViewModel = App.GetService<PageViewModelFactory>().Create<CustomizationConfigurationViewModel>();
        ViewModel.InitializeSection(ConfigurationNavigationTarget.AppxRemoval);
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
